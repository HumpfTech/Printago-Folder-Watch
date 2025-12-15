using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PrintagoFolderWatch.Core.Models;

namespace PrintagoFolderWatch.Core
{
    /// <summary>
    /// Cross-platform file watcher service with:
    /// - Startup cache (fetch all parts/folders once)
    /// - Path-based deletion (exact folder match)
    /// - Change detection (file hash comparison)
    /// - Rate limit friendly (30 ops/min)
    /// </summary>
    public class FileWatcherService : IFileWatcherService, IDisposable
    {
        public Config Config { get; private set; }
        public event Action<string, string>? OnLog;

        private FileSystemWatcher? watcher;
        private readonly ConcurrentQueue<string> uploadQueue = new();
        private readonly ConcurrentQueue<PartCache> deleteQueue = new();
        private readonly ConcurrentQueue<MoveOperation> moveQueue = new();
        private readonly HttpClient httpClient = new();
        private CancellationTokenSource? cts;
        private bool isRunning = false;

        // Caches
        private readonly ConcurrentDictionary<string, List<PartCache>> remoteParts = new();
        private readonly ConcurrentDictionary<string, FolderCache> remoteFolders = new();
        private readonly ConcurrentDictionary<string, LocalFileInfo> localFiles = new();
        private readonly ConcurrentDictionary<string, UploadProgress> activeUploads = new();

        // Tracking database for preserving Part bindings across file moves/renames
        private FileTrackingDb? trackingDb;

        // Pending deletions: track delete events with a grace period for atomic saves
        private readonly ConcurrentDictionary<string, (PartCache part, DateTime deleteTime, string oldHash)> pendingDeletions = new();
        private const int DELETION_GRACE_PERIOD_MS = 1000;

        // Debouncing
        private readonly ConcurrentDictionary<string, DateTime> lastEventTime = new();
        private const int DEBOUNCE_MS = 500;

        // Track files currently being processed
        private readonly ConcurrentDictionary<string, bool> filesInUploadQueue = new();

        // Lock for upload operations on same key
        private readonly ConcurrentDictionary<string, SemaphoreSlim> uploadKeyLocks = new();

        // Root folder for all synced files
        private const string ROOT_SYNC_FOLDER = "Local Folder Sync";
        private string? rootSyncFolderId = null;

        // Folder creation lock
        private readonly SemaphoreSlim folderCreationLock = new SemaphoreSlim(1, 1);

        // Rate limiting
        private const int MAX_PARALLEL_UPLOADS = 10;
        private readonly SemaphoreSlim uploadSemaphore = new SemaphoreSlim(MAX_PARALLEL_UPLOADS, MAX_PARALLEL_UPLOADS);

        // Global API rate limiter
        private readonly SemaphoreSlim apiRateLimiter = new SemaphoreSlim(1, 1);
        private DateTime lastApiCallTime = DateTime.MinValue;

        // Statistics
        private int syncedFilesCount = 0;

        // Recent activity log
        private readonly ConcurrentQueue<string> recentLogs = new();
        private const int MAX_RECENT_LOGS = 50;

        // Public properties for status tracking
        public int UploadQueueCount => uploadQueue.Count;
        public int DeleteQueueCount => deleteQueue.Count;
        public int FoldersCreatedCount => remoteFolders.Count;
        public int SyncedFilesCount => syncedFilesCount;
        public List<UploadProgress> GetActiveUploads() => activeUploads.Values.ToList();

        public List<string> GetDeleteQueueItems()
        {
            return deleteQueue.Select(part =>
            {
                var key = string.IsNullOrEmpty(part.FolderPath)
                    ? part.Name
                    : $"{part.FolderPath}/{part.Name}";
                return key;
            }).ToList();
        }

        public List<MoveOperation> GetMoveQueueItems()
        {
            return moveQueue.ToList();
        }

        public int MoveQueueCount => moveQueue.Count;

        public List<string> GetRecentLogs(int count)
        {
            return recentLogs.Reverse().Take(count).Reverse().ToList();
        }

        public FileWatcherService()
        {
            Config = Config.Load();

            // Initialize tracking database in AppData (writable location)
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrintagoFolderWatch"
            );
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "file-tracking.db");
            trackingDb = new FileTrackingDb(dbPath);
        }

        public async Task<bool> Start()
        {
            if (isRunning || !Config.IsValid())
                return false;

            try
            {
                isRunning = true;
                cts = new CancellationTokenSource();

                Log("Starting file watcher service...", "INFO");

                // PHASE 1: Build initial cache
                await BuildInitialCache();

                // PHASE 1.5: Ensure root sync folder exists
                await EnsureRootSyncFolder();

                // PHASE 2: Scan local files
                await ScanLocalFileSystem();

                // PHASE 3: Perform initial sync
                await PerformInitialSync();

                // PHASE 4: Start file system watcher
                watcher = new FileSystemWatcher(Config.WatchPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };

                watcher.Created += OnFileChanged;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;

                // PHASE 5: Start delete processor
                Task.Run(() => ProcessDeleteQueue(cts.Token));

                // PHASE 6: Start upload processor
                Task.Run(() => ProcessUploadQueue(cts.Token));

                // PHASE 7: Start periodic cache refresh (every 30 min)
                Task.Run(() => PeriodicCacheRefresh(cts.Token));

                Log($"Started watching: {Config.WatchPath}", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to start: {ex.Message}", "ERROR");
                return false;
            }
        }

        public void Stop()
        {
            if (!isRunning)
                return;

            isRunning = false;
            cts?.Cancel();
            watcher?.Dispose();
            watcher = null;

            Log("Stopped watching", "INFO");
        }

        public async Task TriggerSyncNow()
        {
            Log("Manual sync triggered", "INFO");
            await BuildInitialCache();
            await ScanLocalFileSystem();
            await PerformInitialSync();
        }

        public List<string> GetQueueItems()
        {
            return uploadQueue.Select(path =>
            {
                try
                {
                    var relativePath = Path.GetRelativePath(Config.WatchPath, path);
                    return relativePath.Replace("\\", "/");
                }
                catch
                {
                    return Path.GetFileName(path);
                }
            }).ToList();
        }

        #region Rate Limiting Helper

        private async Task<HttpResponseMessage> SendApiRequestAsync(HttpRequestMessage request, int retryCount = 0)
        {
            await apiRateLimiter.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                var timeSinceLastCall = now - lastApiCallTime;
                var minimumDelay = TimeSpan.FromSeconds(2);

                if (timeSinceLastCall < minimumDelay)
                {
                    var waitTime = minimumDelay - timeSinceLastCall;
                    await Task.Delay(waitTime);
                }

                var response = await httpClient.SendAsync(request);
                lastApiCallTime = DateTime.UtcNow;

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < 3)
                {
                    var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, retryCount + 2));
                    Log($"Rate limited (429), retrying in {retryDelay.TotalSeconds}s (attempt {retryCount + 1}/3)", "WARN");
                    await Task.Delay(retryDelay);

                    var retryRequest = new HttpRequestMessage(request.Method, request.RequestUri)
                    {
                        Content = request.Content
                    };
                    foreach (var header in request.Headers)
                    {
                        retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    return await SendApiRequestAsync(retryRequest, retryCount + 1);
                }

                return response;
            }
            finally
            {
                apiRateLimiter.Release();
            }
        }

        #endregion

        #region Phase 1: Build Initial Cache

        private async Task BuildInitialCache()
        {
            Log("========== PHASE 1: BUILD INITIAL CACHE ==========", "INFO");
            Log("Fetching all folders and parts from Printago...", "INFO");

            var apiUrl = Config.ApiUrl.TrimEnd('/');

            var folders = await FetchAllFolders(apiUrl);
            Log($"✓ Fetched {folders.Count} folders from API", "INFO");

            var parts = await FetchAllParts(apiUrl);
            Log($"✓ Fetched {parts.Count} parts from API", "INFO");

            remoteFolders.Clear();
            var folderIdToPath = new Dictionary<string, string>();

            foreach (var folder in folders)
            {
                var path = ReconstructFolderPath(folder.id, folders);
                folderIdToPath[folder.id] = path;

                remoteFolders[path] = new FolderCache
                {
                    Id = folder.id,
                    Name = folder.name,
                    ParentId = folder.parentId,
                    FolderPath = path
                };
            }

            Log($"✓ Built folder cache: {remoteFolders.Count} folders", "INFO");

            remoteParts.Clear();
            int skippedPartsOutsideSync = 0;
            foreach (var part in parts)
            {
                var folderPath = part.folderId != null && folderIdToPath.ContainsKey(part.folderId)
                    ? folderIdToPath[part.folderId]
                    : "";

                bool isInsideLocalFolderSync = folderPath == ROOT_SYNC_FOLDER ||
                                                folderPath.StartsWith($"{ROOT_SYNC_FOLDER}/");

                if (!isInsideLocalFolderSync)
                {
                    skippedPartsOutsideSync++;
                    continue;
                }

                var normalizedFolderPath = folderPath;
                if (!string.IsNullOrEmpty(folderPath) && folderPath.StartsWith($"{ROOT_SYNC_FOLDER}/"))
                {
                    normalizedFolderPath = folderPath.Substring(ROOT_SYNC_FOLDER.Length + 1);
                }
                else if (folderPath == ROOT_SYNC_FOLDER)
                {
                    normalizedFolderPath = "";
                }

                var partKey = string.IsNullOrEmpty(normalizedFolderPath)
                    ? part.name
                    : $"{normalizedFolderPath}/{part.name}";

                var partCache = new PartCache
                {
                    Id = part.id,
                    Name = part.name,
                    FolderId = part.folderId,
                    FolderPath = normalizedFolderPath,
                    FileHash = part.fileHashes?.FirstOrDefault() ?? "",
                    UpdatedAt = part.updatedAt
                };

                remoteParts.AddOrUpdate(partKey,
                    _ => new List<PartCache> { partCache },
                    (_, list) => { list.Add(partCache); return list; });
            }

            if (skippedPartsOutsideSync > 0)
            {
                Log($"✓ Skipped {skippedPartsOutsideSync} parts outside '{ROOT_SYNC_FOLDER}'", "INFO");
            }

            var totalParts = remoteParts.Values.Sum(list => list.Count);
            Log($"✓ Built parts cache: {totalParts} parts", "INFO");
            Log($"========== CACHE COMPLETE ==========", "INFO");
        }

        private async Task<List<FolderDto>> FetchAllFolders(string apiUrl)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/v1/folders?limit=10000");
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Error fetching folders: HTTP {response.StatusCode}", "ERROR");
                    return new List<FolderDto>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var folders = JsonConvert.DeserializeObject<List<FolderDto>>(json);

                return folders ?? new List<FolderDto>();
            }
            catch (Exception ex)
            {
                Log($"Error fetching folders: {ex.Message}", "ERROR");
                return new List<FolderDto>();
            }
        }

        private async Task<List<PartDto>> FetchAllParts(string apiUrl)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/v1/parts?limit=10000");
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Error fetching parts: HTTP {response.StatusCode}", "ERROR");
                    return new List<PartDto>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var parts = JsonConvert.DeserializeObject<List<PartDto>>(json);

                return parts ?? new List<PartDto>();
            }
            catch (Exception ex)
            {
                Log($"Error fetching parts: {ex.Message}", "ERROR");
                return new List<PartDto>();
            }
        }

        private string ReconstructFolderPath(string folderId, List<FolderDto> allFolders)
        {
            var folder = allFolders.FirstOrDefault(f => f.id == folderId);
            if (folder == null)
                return "";

            if (folder.parentId == null)
                return folder.name;

            var parentPath = ReconstructFolderPath(folder.parentId, allFolders);
            return string.IsNullOrEmpty(parentPath)
                ? folder.name
                : $"{parentPath}/{folder.name}";
        }

        private async Task EnsureRootSyncFolder()
        {
            if (remoteFolders.TryGetValue(ROOT_SYNC_FOLDER, out var cached))
            {
                rootSyncFolderId = cached.Id;
                Log($"Found existing '{ROOT_SYNC_FOLDER}' folder", "INFO");
                return;
            }

            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var createBody = new
                {
                    name = ROOT_SYNC_FOLDER,
                    type = "part",
                    parentId = (string?)null
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/folders")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(createBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var created = JsonConvert.DeserializeAnonymousType(json, new { id = "" });

                if (created != null && !string.IsNullOrEmpty(created.id))
                {
                    rootSyncFolderId = created.id;
                    remoteFolders[ROOT_SYNC_FOLDER] = new FolderCache
                    {
                        Id = created.id,
                        Name = ROOT_SYNC_FOLDER,
                        ParentId = null,
                        FolderPath = ROOT_SYNC_FOLDER
                    };
                    Log($"Created '{ROOT_SYNC_FOLDER}' folder", "SUCCESS");
                }
            }
            catch (Exception ex)
            {
                Log($"Error creating '{ROOT_SYNC_FOLDER}' folder: {ex.Message}", "ERROR");
            }
        }

        #endregion

        #region Phase 2: Scan Local Files

        private async Task ScanLocalFileSystem()
        {
            Log("========== PHASE 2: SCAN LOCAL FILES ==========", "INFO");
            Log($"Scanning directory: {Config.WatchPath}", "INFO");
            localFiles.Clear();

            await Task.Run(() =>
            {
                ScanDirectory(Config.WatchPath);
            });

            Log($"✓ Found {localFiles.Count} local files", "INFO");
            Log($"========== SCAN COMPLETE ==========", "INFO");
        }

        private void ScanDirectory(string dirPath)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath))
                {
                    if (IsSupportedFile(file))
                    {
                        AddLocalFile(file);
                    }
                }

                foreach (var subDir in Directory.GetDirectories(dirPath))
                {
                    ScanDirectory(subDir);
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning {dirPath}: {ex.Message}", "ERROR");
            }
        }

        private void AddLocalFile(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var relativePath = Path.GetRelativePath(Config.WatchPath, filePath);
                var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                var partName = Path.GetFileNameWithoutExtension(fileInfo.Name);

                var localFile = new LocalFileInfo
                {
                    FilePath = filePath,
                    RelativePath = relativePath.Replace("\\", "/"),
                    FolderPath = folderPath,
                    FileName = fileInfo.Name,
                    PartName = partName,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    FileHash = ""
                };

                var key = string.IsNullOrEmpty(folderPath)
                    ? partName
                    : $"{folderPath}/{partName}";

                localFiles[key] = localFile;
            }
            catch (Exception ex)
            {
                Log($"Error adding local file {filePath}: {ex.Message}", "ERROR");
            }
        }

        #endregion

        #region Phase 3: Initial Sync

        private async Task PerformInitialSync()
        {
            Log("========== PHASE 3: INITIAL SYNC ==========", "INFO");

            int iteration = 0;
            bool hasChanges = true;

            while (hasChanges && iteration < 10)
            {
                iteration++;
                if (iteration > 1)
                {
                    Log($"========== SYNC ITERATION {iteration} ==========", "INFO");
                }

                var deletions = new List<PartCache>();
                var uploads = new List<LocalFileInfo>();

                Log("STEP 1: Reconciling with tracking database...", "INFO");
                int foldersDeleted = await ReconcileWithTrackingDb();

                Log("STEP 2: Finding remote parts to delete...", "INFO");
                foreach (var kvp in remoteParts)
                {
                    var key = kvp.Key;
                    var partsList = kvp.Value;

                    if (!localFiles.ContainsKey(key))
                    {
                        foreach (var remotePart in partsList)
                        {
                            var tracked = trackingDb?.GetAll().FirstOrDefault(t => t.PartId == remotePart.Id);
                            if (tracked == null)
                            {
                                deletions.Add(remotePart);
                            }
                            else
                            {
                                if (File.Exists(tracked.FilePath))
                                {
                                    Log($"  Skipping deletion of {key} - tracked to {tracked.FilePath}", "DEBUG");
                                }
                                else
                                {
                                    deletions.Add(remotePart);
                                    trackingDb?.Delete(tracked.FilePath);
                                }
                            }
                        }
                    }
                    else if (partsList.Count > 1)
                    {
                        Log($"  Found {partsList.Count} duplicates for: {key}", "INFO");
                        var localFile = localFiles[key];
                        string? localHash = null;
                        try
                        {
                            localHash = ComputeFileHash(localFile.FilePath).Result;
                        }
                        catch { }

                        var sortedParts = partsList
                            .OrderByDescending(p => p.FileHash == localHash)
                            .ThenByDescending(p => p.UpdatedAt)
                            .ToList();

                        var keepPart = sortedParts.First();
                        foreach (var dupPart in sortedParts.Skip(1))
                        {
                            deletions.Add(dupPart);
                        }
                    }
                }

                Log("STEP 3: Finding local files to upload...", "INFO");
                foreach (var localFile in localFiles.Values)
                {
                    var key = string.IsNullOrEmpty(localFile.FolderPath)
                        ? localFile.PartName
                        : $"{localFile.FolderPath}/{localFile.PartName}";

                    if (!remoteParts.ContainsKey(key))
                    {
                        var tracked = trackingDb?.GetByPath(localFile.FilePath);
                        if (tracked != null && !string.IsNullOrEmpty(tracked.PartId))
                        {
                            bool partExistsInPrintago = remoteParts.Values.Any(list => list.Any(p => p.Id == tracked.PartId));
                            if (partExistsInPrintago)
                            {
                                continue;
                            }
                            else
                            {
                                uploads.Add(localFile);
                            }
                        }
                        else
                        {
                            uploads.Add(localFile);
                        }
                    }
                    else
                    {
                        var remotePart = remoteParts[key].First();
                        if (await IsFileChanged(localFile, remotePart))
                        {
                            uploads.Add(localFile);
                        }
                    }
                }

                Log($"✓ Sync plan: {deletions.Count} deletions, {uploads.Count} uploads", "INFO");

                if (foldersDeleted > 0)
                {
                    Log($"Folders deleted: {foldersDeleted} - refreshing cache...", "INFO");
                    hasChanges = true;
                    await BuildInitialCache();
                    continue;
                }

                hasChanges = false;
                Log($"========== SYNC PLAN COMPLETE ==========", "INFO");

                foreach (var part in deletions)
                {
                    deleteQueue.Enqueue(part);
                }

                foreach (var file in uploads)
                {
                    uploadQueue.Enqueue(file.FilePath);
                }
            }

            Log("========== ALL SYNC ITERATIONS COMPLETE ==========", "INFO");
        }

        private async Task<int> ReconcileWithTrackingDb()
        {
            if (trackingDb == null)
                return 0;

            Log("Reconciling files with tracking database...", "INFO");
            int matched = 0, updated = 0;

            foreach (var localFile in localFiles.Values)
            {
                if (string.IsNullOrEmpty(localFile.FileHash))
                {
                    localFile.FileHash = await ComputeFileHash(localFile.FilePath);
                }

                var trackedByPath = trackingDb.GetByPath(localFile.FilePath);

                if (trackedByPath != null)
                {
                    if (trackedByPath.FileHash != localFile.FileHash)
                    {
                        Log($"File content changed: {localFile.PartName}", "INFO");
                        trackingDb.UpdateHash(localFile.FilePath, localFile.FileHash);
                        updated++;
                    }
                    continue;
                }

                var trackedByHash = trackingDb.GetByHash(localFile.FileHash);

                if (trackedByHash != null && trackedByHash.FilePath != localFile.FilePath)
                {
                    Log($"File move detected: {trackedByHash.PartName}", "INFO");
                    trackingDb.UpdatePath(trackedByHash.FilePath, localFile.FilePath);

                    try
                    {
                        await UpdatePartFolder(trackedByHash.PartId, localFile.FolderPath);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to update Part folder: {ex.Message}", "ERROR");
                    }

                    matched++;
                    continue;
                }

                var key = string.IsNullOrEmpty(localFile.FolderPath)
                    ? localFile.PartName
                    : $"{localFile.FolderPath}/{localFile.PartName}";

                if (remoteParts.TryGetValue(key, out var remotePartsList) && remotePartsList.Any(p => p.FileHash == localFile.FileHash))
                {
                    var remotePart = remotePartsList.First(p => p.FileHash == localFile.FileHash);
                    if (remotePart.FolderPath != localFile.FolderPath)
                    {
                        try
                        {
                            await UpdatePartFolder(remotePart.Id, localFile.FolderPath);
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to update Part folder: {ex.Message}", "ERROR");
                        }
                    }

                    trackingDb.Upsert(new FileTrackingEntry
                    {
                        FilePath = localFile.FilePath,
                        FileHash = localFile.FileHash,
                        PartId = remotePart.Id,
                        PartName = localFile.PartName,
                        FolderPath = localFile.FolderPath,
                        LastSeenAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                    matched++;
                    continue;
                }

                var matchByHash = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.FileHash) &&
                    !string.IsNullOrEmpty(localFile.FileHash) &&
                    p.FileHash == localFile.FileHash &&
                    p.Name == localFile.PartName);
                if (matchByHash != null)
                {
                    try
                    {
                        await UpdatePartFolder(matchByHash.Id, localFile.FolderPath);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to update Part folder: {ex.Message}", "ERROR");
                    }

                    trackingDb.Upsert(new FileTrackingEntry
                    {
                        FilePath = localFile.FilePath,
                        FileHash = localFile.FileHash,
                        PartId = matchByHash.Id,
                        PartName = localFile.PartName,
                        FolderPath = localFile.FolderPath,
                        LastSeenAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                    matched++;
                }
            }

            Log($"Reconciliation complete: {matched} matches, {updated} updates", "INFO");

            int foldersDeleted = await CleanupEmptyFolders();
            return foldersDeleted;
        }

        private async Task<bool> IsFileChanged(LocalFileInfo localFile, PartCache remotePart)
        {
            try
            {
                if (string.IsNullOrEmpty(localFile.FileHash))
                {
                    localFile.FileHash = await ComputeFileHash(localFile.FilePath);
                }
                return localFile.FileHash != remotePart.FileHash;
            }
            catch
            {
                return true;
            }
        }

        private async Task<string> ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private async Task DeletePart(PartCache part)
        {
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{apiUrl}/v1/parts/{part.Id}");
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                await SendApiRequestAsync(request);

                var key = string.IsNullOrEmpty(part.FolderPath)
                    ? part.Name
                    : $"{part.FolderPath}/{part.Name}";

                remoteParts.TryRemove(key, out _);

                Log($"Deleted: {key}", "INFO");
            }
            catch (Exception ex)
            {
                Log($"Error deleting part {part.Name}: {ex.Message}", "ERROR");
            }
        }

        private async Task UpdatePartFolder(string partId, string newFolderPath)
        {
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var newFolderId = await GetOrCreateFolder(newFolderPath);

                if (newFolderId == null)
                {
                    Log($"Cannot move Part {partId} - failed to get/create folder", "ERROR");
                    return;
                }

                var updateBody = new { folderId = newFolderId };

                var request = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}/v1/parts/{partId}")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(updateBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var partName = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p => p.Id == partId)?.Name ?? partId;
                    Log($"✓ MOVED: '{partName}' → '{newFolderPath}'", "MOVE");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Failed to move Part {partId}: HTTP {response.StatusCode}", "ERROR");
                }
            }
            catch (Exception ex)
            {
                Log($"Error updating Part {partId} folder: {ex.Message}", "ERROR");
            }
        }

        private async Task<bool> UpdatePartFile(string partId, string filePath, string storagePath)
        {
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var updateBody = new { fileUris = new[] { storagePath } };

                var request = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}/v1/parts/{partId}")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(updateBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log($"✓ Updated Part {partId} with new file", "SUCCESS");
                    return true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Failed to update Part {partId} file: HTTP {response.StatusCode}", "ERROR");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error updating Part {partId} file: {ex.Message}", "ERROR");
                return false;
            }
        }

        #endregion

        #region File System Events

        private bool IsSupportedFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLower();

            if (fileName.EndsWith(".gcode.3mf"))
                return true;

            var ext = Path.GetExtension(filePath).ToLower();
            return ext == ".stl" || ext == ".3mf" || ext == ".scad" || ext == ".step" || ext == ".stp";
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsSupportedFile(e.FullPath))
            {
                var now = DateTime.UtcNow;
                if (lastEventTime.TryGetValue(e.FullPath, out var lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < DEBOUNCE_MS)
                    {
                        return;
                    }
                }
                lastEventTime[e.FullPath] = now;

                AddLocalFile(e.FullPath);

                try
                {
                    var fileInfo = new FileInfo(e.FullPath);
                    var relativePath = Path.GetRelativePath(Config.WatchPath, e.FullPath);
                    var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                    var partName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    var fileHash = await ComputeFileHash(e.FullPath);

                    var tracked = trackingDb?.GetByHash(fileHash);
                    if (tracked != null)
                    {
                        Log($"Detected file copy: {partName}", "INFO");

                        trackingDb?.Upsert(new FileTrackingEntry
                        {
                            FilePath = e.FullPath,
                            FileHash = fileHash,
                            PartId = tracked.PartId,
                            PartName = partName,
                            FolderPath = folderPath,
                            LastSeenAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });

                        await UpdatePartFolder(tracked.PartId, folderPath);
                        return;
                    }

                    var existingByHash = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p =>
                        !string.IsNullOrEmpty(p.FileHash) &&
                        p.FileHash == fileHash &&
                        p.Name == partName);

                    if (existingByHash != null)
                    {
                        Log($"Detected file copy: {partName}", "INFO");

                        trackingDb?.Upsert(new FileTrackingEntry
                        {
                            FilePath = e.FullPath,
                            FileHash = fileHash,
                            PartId = existingByHash.Id,
                            PartName = partName,
                            FolderPath = folderPath,
                            LastSeenAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });

                        await UpdatePartFolder(existingByHash.Id, folderPath);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error checking for duplicate: {ex.Message}", "WARN");
                }

                if (filesInUploadQueue.TryAdd(e.FullPath, true))
                {
                    uploadQueue.Enqueue(e.FullPath);
                    Log($"Detected change: {Path.GetFileName(e.FullPath)}", "INFO");
                }
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (IsSupportedFile(e.FullPath))
            {
                var relativePath = Path.GetRelativePath(Config.WatchPath, e.FullPath);
                var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                var partName = Path.GetFileNameWithoutExtension(e.Name);

                var key = string.IsNullOrEmpty(folderPath)
                    ? partName
                    : $"{folderPath}/{partName}";

                localFiles.TryRemove(key, out _);

                var tracked = trackingDb?.GetByPath(e.FullPath);
                var oldHash = tracked?.FileHash ?? "";

                if (remoteParts.TryGetValue(key, out var remotePartList) && remotePartList.Any())
                {
                    var remotePart = remotePartList.First();
                    pendingDeletions[e.FullPath] = (remotePart, DateTime.UtcNow, oldHash);
                    Log($"Detected deletion: {e.Name} (grace period)", "INFO");

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(DELETION_GRACE_PERIOD_MS);

                        if (File.Exists(e.FullPath))
                        {
                            if (pendingDeletions.TryRemove(e.FullPath, out var pendingInfo))
                            {
                                Log($"File reappeared: {e.Name} (atomic save)", "INFO");

                                try
                                {
                                    var newHash = await ComputeFileHash(e.FullPath);
                                    if (newHash != pendingInfo.oldHash)
                                    {
                                        uploadQueue.Enqueue(e.FullPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"Error checking hash: {ex.Message}", "WARN");
                                    uploadQueue.Enqueue(e.FullPath);
                                }
                            }
                        }
                        else
                        {
                            if (pendingDeletions.TryRemove(e.FullPath, out var pendingInfo))
                            {
                                Log($"Confirmed deletion: {e.Name}", "INFO");
                                trackingDb?.Delete(e.FullPath);
                                deleteQueue.Enqueue(pendingInfo.part);
                            }
                        }
                    });
                }
                else
                {
                    trackingDb?.Delete(e.FullPath);
                }
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsSupportedFile(e.FullPath))
            {
                Log($"Detected rename: {e.OldName} → {e.Name}", "INFO");

                var oldRelativePath = Path.GetRelativePath(Config.WatchPath, e.OldFullPath);
                var oldFolderPath = Path.GetDirectoryName(oldRelativePath)?.Replace("\\", "/") ?? "";
                var oldPartName = Path.GetFileNameWithoutExtension(e.OldName);
                var oldKey = string.IsNullOrEmpty(oldFolderPath)
                    ? oldPartName
                    : $"{oldFolderPath}/{oldPartName}";
                localFiles.TryRemove(oldKey, out _);

                AddLocalFile(e.FullPath);

                OnFileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath) ?? "", Path.GetFileName(e.FullPath)));
            }
            else if (Directory.Exists(e.FullPath))
            {
                Log($"Detected folder rename: {e.OldName} → {e.Name}", "INFO");

                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    await TriggerSyncNow();
                });
            }
        }

        #endregion

        #region Delete Processing

        private async Task ProcessDeleteQueue(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (deleteQueue.TryDequeue(out var part))
                {
                    await DeletePart(part);
                }

                await Task.Delay(2000, ct);
            }
        }

        #endregion

        #region Upload Processing

        private async Task ProcessUploadQueue(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (uploadQueue.TryDequeue(out var filePath))
                {
                    _ = Task.Run(() => ProcessSingleUpload(filePath, ct), ct);
                }

                await Task.Delay(2000, ct);
            }
        }

        private async Task ProcessSingleUpload(string filePath, CancellationToken ct)
        {
            await uploadSemaphore.WaitAsync(ct);
            try
            {
                await Task.Delay(2000, ct);

                if (File.Exists(filePath))
                {
                    await UploadFile(filePath);
                }
            }
            finally
            {
                uploadSemaphore.Release();
                filesInUploadQueue.TryRemove(filePath, out _);
            }
        }

        private async Task UploadFile(string filePath)
        {
            var relativePath = Path.GetRelativePath(Config.WatchPath, filePath);
            var fileName = Path.GetFileName(filePath);
            var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
            var partName = Path.GetFileNameWithoutExtension(fileName);
            var fileExt = Path.GetExtension(filePath).ToLower();

            var progress = new UploadProgress
            {
                FilePath = filePath,
                FileName = fileName,
                RelativePath = relativePath.Replace("\\", "/"),
                ProgressPercent = 0,
                Status = "Starting...",
                StartTime = DateTime.Now,
                FileSizeBytes = new FileInfo(filePath).Length
            };

            activeUploads[filePath] = progress;

            var key = string.IsNullOrEmpty(folderPath)
                ? partName
                : $"{folderPath}/{partName}";

            var keyLock = uploadKeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync();

            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');

                PartCache? existingPart = null;
                bool isUpdate = false;

                if (remoteParts.TryGetValue(key, out var existingPartsList) && existingPartsList.Any())
                {
                    existingPart = existingPartsList.First();
                    progress.Status = "Checking for changes...";
                    progress.ProgressPercent = 5;

                    var localHash = await ComputeFileHash(filePath);

                    if (localHash == existingPart.FileHash)
                    {
                        progress.Status = "Already up-to-date";
                        progress.ProgressPercent = 100;
                        Log($"Skipped: {key} (up-to-date)", "INFO");
                        await Task.Delay(1000);
                        activeUploads.TryRemove(filePath, out _);
                        return;
                    }

                    isUpdate = true;
                    Log($"File changed: {key} - updating", "INFO");
                }

                progress.Status = "Creating folders...";
                progress.ProgressPercent = 10;
                string? folderId = await GetOrCreateFolder(folderPath);

                progress.Status = "Reading file...";
                progress.ProgressPercent = 15;
                var fileBytes = await File.ReadAllBytesAsync(filePath);

                progress.Status = "Getting signed URL...";
                progress.ProgressPercent = 20;

                var cloudPath = relativePath.Replace("\\", "/");
                var signedUrlResponse = await GetSignedUploadUrl(apiUrl, cloudPath);

                if (signedUrlResponse == null)
                {
                    progress.Status = "Failed - No signed URL";
                    Log($"Failed: {key} - No signed URL", "ERROR");
                    activeUploads.TryRemove(filePath, out _);
                    return;
                }

                progress.Status = "Uploading...";
                progress.ProgressPercent = 40;

                var uploadRequest = new HttpRequestMessage(HttpMethod.Put, signedUrlResponse.Value.uploadUrl)
                {
                    Content = new ByteArrayContent(fileBytes)
                };

                var uploadResponse = await httpClient.SendAsync(uploadRequest);
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    progress.Status = $"Upload failed: {uploadResponse.StatusCode}";
                    Log($"Upload failed: {key}", "ERROR");
                    activeUploads.TryRemove(filePath, out _);
                    return;
                }

                string? partId = null;

                if (isUpdate && existingPart != null)
                {
                    progress.Status = "Updating part...";
                    progress.ProgressPercent = 80;

                    bool updateSuccess = await UpdatePartFile(existingPart.Id, filePath, signedUrlResponse.Value.storagePath);

                    if (updateSuccess)
                    {
                        partId = existingPart.Id;

                        var fileHash = await ComputeFileHash(filePath);
                        remoteParts[key] = new List<PartCache> { new PartCache
                        {
                            Id = partId,
                            Name = partName,
                            FolderId = folderId,
                            FolderPath = folderPath,
                            FileHash = fileHash,
                            UpdatedAt = DateTime.UtcNow
                        } };

                        trackingDb?.Upsert(new FileTrackingEntry
                        {
                            FilePath = filePath,
                            FileHash = fileHash,
                            PartId = partId,
                            PartName = partName,
                            FolderPath = folderPath,
                            LastSeenAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });

                        progress.Status = "Complete!";
                        progress.ProgressPercent = 100;
                        Log($"Updated: {key} (Part ID: {partId})", "SUCCESS");
                        Interlocked.Increment(ref syncedFilesCount);
                    }
                    else
                    {
                        progress.Status = "Failed to update part";
                        Log($"Failed to update part: {key}", "ERROR");
                    }
                }
                else
                {
                    progress.Status = "Creating part...";
                    progress.ProgressPercent = 80;

                    var partType = fileExt == ".3mf" ? "3mf" : "stl";
                    var partBody = new
                    {
                        name = partName,
                        type = partType,
                        description = "Auto-uploaded from folder watch",
                        fileUris = new[] { signedUrlResponse.Value.storagePath },
                        parameters = new object[0],
                        printTags = new { },
                        overriddenProcessProfileId = (string?)null,
                        folderId = folderId
                    };

                    var partRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/parts")
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(partBody), Encoding.UTF8, "application/json")
                    };
                    partRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                    partRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                    var partResponse = await SendApiRequestAsync(partRequest);
                    if (partResponse.IsSuccessStatusCode)
                    {
                        var partResponseJson = await partResponse.Content.ReadAsStringAsync();
                        var createdPart = JsonConvert.DeserializeAnonymousType(partResponseJson, new { id = "" });
                        partId = createdPart?.id ?? "";

                        var fileHash = await ComputeFileHash(filePath);
                        remoteParts[key] = new List<PartCache> { new PartCache
                        {
                            Id = partId,
                            Name = partName,
                            FolderId = folderId,
                            FolderPath = folderPath,
                            FileHash = fileHash,
                            UpdatedAt = DateTime.UtcNow
                        } };

                        trackingDb?.Upsert(new FileTrackingEntry
                        {
                            FilePath = filePath,
                            FileHash = fileHash,
                            PartId = partId,
                            PartName = partName,
                            FolderPath = folderPath,
                            LastSeenAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });

                        progress.Status = "Complete!";
                        progress.ProgressPercent = 100;
                        Log($"Uploaded: {key} (Part ID: {partId})", "SUCCESS");
                        Interlocked.Increment(ref syncedFilesCount);
                    }
                    else
                    {
                        progress.Status = $"Failed to create part: {partResponse.StatusCode}";
                        Log($"Failed to create part: {key}", "ERROR");
                    }
                }

                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                progress.Status = $"Error: {ex.Message}";
                Log($"Upload error: {fileName} - {ex.Message}", "ERROR");
            }
            finally
            {
                activeUploads.TryRemove(filePath, out _);
                keyLock.Release();
            }
        }

        private async Task<(string uploadUrl, string storagePath)?> GetSignedUploadUrl(string apiUrl, string cloudPath)
        {
            try
            {
                var requestBody = new { filenames = new[] { cloudPath } };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/storage/signed-upload-urls")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeAnonymousType(json, new
                {
                    signedUrls = new[] { new { uploadUrl = "", path = "" } }
                });

                if (result?.signedUrls != null && result.signedUrls.Length > 0)
                {
                    return (result.signedUrls[0].uploadUrl, result.signedUrls[0].path);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> GetOrCreateFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                await EnsureRootSyncFolder();
                return rootSyncFolderId;
            }

            var fullPath = $"{ROOT_SYNC_FOLDER}/{folderPath}";

            if (remoteFolders.TryGetValue(fullPath, out var cached))
                return cached.Id;

            if (remoteFolders.TryGetValue(folderPath, out var legacyFolder))
            {
                Log($"Using existing root-level folder '{folderPath}'", "INFO");
                return legacyFolder.Id;
            }

            await folderCreationLock.WaitAsync();
            try
            {
                if (remoteFolders.TryGetValue(fullPath, out var recheck))
                    return recheck.Id;

                var apiUrl = Config.ApiUrl.TrimEnd('/');

                await EnsureRootSyncFolder();

                string? parentId = rootSyncFolderId;
                string currentPath = ROOT_SYNC_FOLDER;

                var subParts = folderPath.Split('/');
                foreach (var folderName in subParts)
                {
                    currentPath = $"{currentPath}/{folderName}";

                    if (remoteFolders.TryGetValue(currentPath, out var existing))
                    {
                        parentId = existing.Id;
                        continue;
                    }

                    try
                    {
                        var createBody = new
                        {
                            name = folderName,
                            type = "part",
                            parentId = parentId
                        };

                        var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/folders")
                        {
                            Content = new StringContent(JsonConvert.SerializeObject(createBody), Encoding.UTF8, "application/json")
                        };
                        request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                        request.Headers.Add("x-printago-storeid", Config.StoreId);

                        var response = await SendApiRequestAsync(request);
                        var json = await response.Content.ReadAsStringAsync();
                        var created = JsonConvert.DeserializeAnonymousType(json, new { id = "" });

                        if (created != null && !string.IsNullOrEmpty(created.id))
                        {
                            remoteFolders[currentPath] = new FolderCache
                            {
                                Id = created.id,
                                Name = folderName,
                                ParentId = parentId,
                                FolderPath = currentPath
                            };
                            parentId = created.id;
                            Log($"Created folder: {currentPath}", "INFO");
                        }
                        else
                        {
                            Log($"Failed to create folder {currentPath}", "ERROR");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error creating folder {currentPath}: {ex.Message}", "ERROR");
                        return null;
                    }
                }

                return parentId;
            }
            finally
            {
                folderCreationLock.Release();
            }
        }

        #endregion

        #region Folder Cleanup

        private async Task<int> CleanupEmptyFolders()
        {
            int deletedCount = 0;
            try
            {
                Log("Checking for empty folders...", "INFO");

                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var folders = await FetchAllFolders(apiUrl);
                var parts = await FetchAllParts(apiUrl);

                var foldersWithParts = new HashSet<string>();
                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part.folderId))
                    {
                        foldersWithParts.Add(part.folderId);
                    }
                }

                var foldersWithChildren = new HashSet<string>();
                foreach (var folder in folders)
                {
                    if (!string.IsNullOrEmpty(folder.parentId))
                    {
                        foldersWithChildren.Add(folder.parentId);
                    }
                }

                var localFolderPaths = new HashSet<string>();
                foreach (var localFile in localFiles.Values)
                {
                    if (!string.IsNullOrEmpty(localFile.FolderPath))
                    {
                        var parts_path = localFile.FolderPath.Split('/');
                        var currentPath = "";
                        foreach (var part_name in parts_path)
                        {
                            currentPath = string.IsNullOrEmpty(currentPath) ? part_name : $"{currentPath}/{part_name}";
                            localFolderPaths.Add(currentPath);
                        }
                    }
                }

                var foldersToDelete = new List<FolderDto>();

                foreach (var folder in folders)
                {
                    if (folder.name == ROOT_SYNC_FOLDER && folder.parentId == null)
                        continue;

                    bool isEmpty = !foldersWithParts.Contains(folder.id) && !foldersWithChildren.Contains(folder.id);

                    if (isEmpty)
                    {
                        var folderPath = ReconstructFolderPath(folder.id, folders);

                        var normalizedPath = folderPath;
                        if (folderPath.StartsWith($"{ROOT_SYNC_FOLDER}/"))
                        {
                            normalizedPath = folderPath.Substring(ROOT_SYNC_FOLDER.Length + 1);
                        }

                        bool existsLocally = localFolderPaths.Contains(normalizedPath);

                        if (!existsLocally)
                        {
                            foldersToDelete.Add(folder);
                        }
                    }
                }

                if (foldersToDelete.Count > 0)
                {
                    Log($"Found {foldersToDelete.Count} empty folders to delete", "INFO");

                    var sortedFolders = foldersToDelete.OrderByDescending(f => ReconstructFolderPath(f.id, folders).Count(c => c == '/')).ToList();

                    foreach (var folder in sortedFolders)
                    {
                        try
                        {
                            var folderPath = ReconstructFolderPath(folder.id, folders);

                            var deleteBody = new
                            {
                                folderIds = new[] { folder.id },
                                type = "part"
                            };

                            var request = new HttpRequestMessage(HttpMethod.Delete, $"{apiUrl}/v1/folders/delete")
                            {
                                Content = new StringContent(JsonConvert.SerializeObject(deleteBody), Encoding.UTF8, "application/json")
                            };
                            request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                            request.Headers.Add("x-printago-storeid", Config.StoreId);

                            var response = await SendApiRequestAsync(request);

                            if (response.IsSuccessStatusCode)
                            {
                                Log($"✓ Deleted empty folder: '{folderPath}'", "SUCCESS");
                                remoteFolders.TryRemove(folderPath, out _);
                                deletedCount++;
                            }
                            else
                            {
                                var errorBody = await response.Content.ReadAsStringAsync();
                                Log($"Failed to delete folder '{folderPath}': HTTP {response.StatusCode}", "ERROR");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error deleting folder {folder.name}: {ex.Message}", "ERROR");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Folder cleanup error: {ex.Message}", "ERROR");
            }

            return deletedCount;
        }

        #endregion

        #region Periodic Tasks

        private async Task PeriodicCacheRefresh(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), ct);

                try
                {
                    Log("Refreshing cache...", "INFO");
                    await BuildInitialCache();
                    await ScanLocalFileSystem();
                    await PerformInitialSync();
                }
                catch (Exception ex)
                {
                    Log($"Cache refresh error: {ex.Message}", "ERROR");
                }
            }
        }

        #endregion

        private void Log(string message, string level)
        {
            OnLog?.Invoke(message, level);

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            recentLogs.Enqueue(logEntry);

            while (recentLogs.Count > MAX_RECENT_LOGS)
            {
                recentLogs.TryDequeue(out _);
            }

            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PrintagoFolderWatch"
                );
                var logDir = Path.Combine(appDataPath, "logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, $"printago-{DateTime.Now:yyyy-MM-dd}.log");
                var fullTimestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                File.AppendAllText(logFile, $"[{fullTimestamp}] [{level}] {message}\n");
            }
            catch
            {
                // Ignore file logging errors
            }
        }

        public void Dispose()
        {
            Stop();
            httpClient?.Dispose();
            cts?.Dispose();
            trackingDb?.Dispose();
        }
    }
}
