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

namespace PrintagoFolderWatch
{
    /// <summary>
    /// Efficient file watcher service with:
    /// - Startup cache (fetch all parts/folders once)
    /// - Path-based deletion (exact folder match)
    /// - Change detection (file hash comparison)
    /// - Rate limit friendly (30 ops/min)
    /// </summary>
    public class FileWatcherServiceV2 : IFileWatcherService, IDisposable
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
        private readonly ConcurrentDictionary<string, List<PartCache>> remoteParts = new(); // key = "folderPath/partName", value = list of parts (supports duplicates)
        private readonly ConcurrentDictionary<string, FolderCache> remoteFolders = new(); // key = folderPath
        private readonly ConcurrentDictionary<string, LocalFileInfo> localFiles = new();
        private readonly ConcurrentDictionary<string, UploadProgress> activeUploads = new();

        // Tracking database for preserving Part bindings across file moves/renames
        private FileTrackingDb? trackingDb;

        // Pending deletions: track delete events with a grace period for atomic saves
        // Key = file path, Value = (Part to delete, Time when deletion was requested)
        private readonly ConcurrentDictionary<string, (PartCache part, DateTime deleteTime, string oldHash)> pendingDeletions = new();
        private const int DELETION_GRACE_PERIOD_MS = 1000; // 1 second grace period for atomic saves

        // Debouncing: track last event time for each file to prevent duplicate events from FileSystemWatcher
        // FileSystemWatcher often fires multiple events for a single file operation
        private readonly ConcurrentDictionary<string, DateTime> lastEventTime = new();
        private const int DEBOUNCE_MS = 500; // Ignore duplicate events within 500ms

        // Track files currently being processed to prevent duplicate uploads
        private readonly ConcurrentDictionary<string, bool> filesInUploadQueue = new();

        // Lock for upload operations on same key to prevent race condition duplicates
        private readonly ConcurrentDictionary<string, SemaphoreSlim> uploadKeyLocks = new();

        // Root folder for all synced files
        private const string ROOT_SYNC_FOLDER = "Local Folder Sync";
        private string? rootSyncFolderId = null;

        // Folder creation lock to prevent duplicate folders from concurrent uploads
        private readonly SemaphoreSlim folderCreationLock = new SemaphoreSlim(1, 1);

        // Rate limiting
        private const int MAX_PARALLEL_UPLOADS = 10; // Allow 10 concurrent uploads
        private readonly SemaphoreSlim uploadSemaphore = new SemaphoreSlim(MAX_PARALLEL_UPLOADS, MAX_PARALLEL_UPLOADS);

        // Global API rate limiter: 30 requests per minute = 1 request every 2 seconds
        private readonly SemaphoreSlim apiRateLimiter = new SemaphoreSlim(1, 1);
        private DateTime lastApiCallTime = DateTime.MinValue;

        // Statistics
        private int syncedFilesCount = 0;

        // Recent activity log (keep last 50 entries)
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

        public FileWatcherServiceV2()
        {
            Config = Config.Load();

            // Initialize tracking database in AppData (writable location)
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrintagoFolderWatch"
            );
            Directory.CreateDirectory(appDataPath); // Ensure directory exists
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

                // PHASE 5: Start delete processor (process before uploads)
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

        /// <summary>
        /// Ensures ALL API calls respect the rate limit of 30 requests per minute (1 every 2 seconds)
        /// Includes retry logic for 429 rate limit errors
        /// </summary>
        private async Task<HttpResponseMessage> SendApiRequestAsync(HttpRequestMessage request, int retryCount = 0)
        {
            await apiRateLimiter.WaitAsync();
            try
            {
                // Calculate time since last API call
                var now = DateTime.UtcNow;
                var timeSinceLastCall = now - lastApiCallTime;
                var minimumDelay = TimeSpan.FromSeconds(2); // 30 requests per minute

                // If less than 2 seconds since last call, wait
                if (timeSinceLastCall < minimumDelay)
                {
                    var waitTime = minimumDelay - timeSinceLastCall;
                    await Task.Delay(waitTime);
                }

                // Make the API call
                var response = await httpClient.SendAsync(request);
                lastApiCallTime = DateTime.UtcNow;

                // Handle 429 Too Many Requests with exponential backoff
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < 3)
                {
                    var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, retryCount + 2)); // 4s, 8s, 16s
                    Log($"Rate limited (429), retrying in {retryDelay.TotalSeconds}s (attempt {retryCount + 1}/3)", "WARN");
                    await Task.Delay(retryDelay);

                    // Recreate the request (can't reuse consumed request)
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

            // Fetch all folders first
            var folders = await FetchAllFolders(apiUrl);
            Log($"✓ Fetched {folders.Count} folders from API", "INFO");

            // Then fetch all parts (sequential to respect rate limits)
            var parts = await FetchAllParts(apiUrl);
            Log($"✓ Fetched {parts.Count} parts from API", "INFO");

            // Build folder cache with paths
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
            foreach (var folder in remoteFolders.Values.Take(5))
            {
                Log($"  Sample folder: '{folder.FolderPath}' (ID: {folder.Id})", "DEBUG");
            }

            // Build parts cache with paths
            remoteParts.Clear();
            foreach (var part in parts)
            {
                var folderPath = part.folderId != null && folderIdToPath.ContainsKey(part.folderId)
                    ? folderIdToPath[part.folderId]
                    : "";

                // Strip "Local Folder Sync" prefix to match local file keys
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
                    FolderPath = normalizedFolderPath, // Store normalized path
                    FileHash = part.fileHashes?.FirstOrDefault() ?? "",
                    UpdatedAt = part.updatedAt
                };

                // Add to list (supports duplicates with same key)
                remoteParts.AddOrUpdate(partKey,
                    _ => new List<PartCache> { partCache },
                    (_, list) => { list.Add(partCache); return list; });
            }

            // Count total parts including duplicates
            var totalParts = remoteParts.Values.Sum(list => list.Count);
            var duplicateKeys = remoteParts.Where(kvp => kvp.Value.Count > 1).Count();
            Log($"✓ Built parts cache: {totalParts} parts in {remoteParts.Count} unique keys ({duplicateKeys} keys have duplicates)", "INFO");
            foreach (var kvp in remoteParts.Take(5))
            {
                var first = kvp.Value.First();
                Log($"  Sample part: Key='{kvp.Key}' | Name='{first.Name}' | Folder='{first.FolderPath}' | Hash={first.FileHash.Substring(0, Math.Min(8, first.FileHash.Length))} | Copies={kvp.Value.Count}", "DEBUG");
            }

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
                    Log($"Response body: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}", "ERROR");
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
                    Log($"Response body: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}", "ERROR");
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

        /// <summary>
        /// Ensures the root "Local Folder Sync" folder exists in Printago
        /// </summary>
        private async Task EnsureRootSyncFolder()
        {
            // Check if already exists in cache
            if (remoteFolders.TryGetValue(ROOT_SYNC_FOLDER, out var cached))
            {
                rootSyncFolderId = cached.Id;
                Log($"Found existing '{ROOT_SYNC_FOLDER}' folder", "INFO");
                return;
            }

            // Create the root sync folder
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var createBody = new
                {
                    name = ROOT_SYNC_FOLDER,
                    type = "part",
                    parentId = (string?)null // Root level folder
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
            foreach (var kvp in localFiles.Take(5))
            {
                Log($"  Sample file: Key='{kvp.Key}' | Name='{kvp.Value.PartName}' | Folder='{kvp.Value.FolderPath}'", "DEBUG");
            }
            Log($"========== SCAN COMPLETE ==========", "INFO");
        }

        private void ScanDirectory(string dirPath)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".3mf" || ext == ".stl")
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
                    FileHash = "" // Will compute on demand
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

            // Loop until no changes are detected (handles cascading folder deletions)
            int iteration = 0;
            bool hasChanges = true;

            while (hasChanges && iteration < 10) // Max 10 iterations to prevent infinite loops
            {
                iteration++;
                if (iteration > 1)
                {
                    Log($"========== SYNC ITERATION {iteration} ==========", "INFO");
                }

                var deletions = new List<PartCache>();
                var uploads = new List<LocalFileInfo>();

                // STEP 1: Reconcile local files with tracking DB
                Log("STEP 1: Reconciling with tracking database...", "INFO");
                int foldersDeleted = await ReconcileWithTrackingDb();

            // STEP 2: Find parts to delete (remote parts not in local files, not tracked, OR duplicates)
            Log("STEP 2: Finding remote parts to delete...", "INFO");
            foreach (var kvp in remoteParts)
            {
                var key = kvp.Key;
                var partsList = kvp.Value;

                if (!localFiles.ContainsKey(key))
                {
                    // No local file for this key - delete ALL remote parts with this key
                    foreach (var remotePart in partsList)
                    {
                        // Check if this Part is tracked to a different path (file was moved)
                        var tracked = trackingDb?.GetAll().FirstOrDefault(t => t.PartId == remotePart.Id);
                        if (tracked == null)
                        {
                            // Not tracked anywhere, safe to delete
                            deletions.Add(remotePart);
                            Log($"  Will delete: {key} (ID: {remotePart.Id}) (not found locally, not tracked)", "DEBUG");
                        }
                        else
                        {
                            // Tracked, but verify the file actually exists
                            if (File.Exists(tracked.FilePath))
                            {
                                Log($"  Skipping deletion of {key} - tracked to {tracked.FilePath}", "DEBUG");
                            }
                            else
                            {
                                // Stale tracking entry - file no longer exists
                                Log($"  Will delete: {key} (ID: {remotePart.Id}) (tracked file no longer exists: {tracked.FilePath})", "DEBUG");
                                deletions.Add(remotePart);

                                // Clean up stale tracking entry
                                trackingDb?.Delete(tracked.FilePath);
                            }
                        }
                    }
                }
                else if (partsList.Count > 1)
                {
                    // Local file exists, but there are DUPLICATE remote parts - keep only the best one
                    Log($"  Found {partsList.Count} duplicates for: {key}", "INFO");

                    // Get the local file hash to find the best matching part
                    var localFile = localFiles[key];
                    string? localHash = null;
                    try
                    {
                        localHash = ComputeFileHash(localFile.FilePath).Result;
                    }
                    catch { }

                    // Sort parts: prefer ones with matching hash, then by most recent
                    var sortedParts = partsList
                        .OrderByDescending(p => p.FileHash == localHash) // Matching hash first
                        .ThenByDescending(p => p.UpdatedAt) // Most recent second
                        .ToList();

                    // Keep the first one, delete the rest
                    var keepPart = sortedParts.First();
                    Log($"    Keeping: {keepPart.Id} (hash match: {keepPart.FileHash == localHash}, updated: {keepPart.UpdatedAt})", "DEBUG");

                    foreach (var dupPart in sortedParts.Skip(1))
                    {
                        deletions.Add(dupPart);
                        Log($"    Will delete duplicate: {dupPart.Id} (hash match: {dupPart.FileHash == localHash}, updated: {dupPart.UpdatedAt})", "DEBUG");
                    }
                }
            }

            // STEP 3: Find files to upload (new or changed)
            Log("STEP 3: Finding local files to upload...", "INFO");
            foreach (var localFile in localFiles.Values)
            {
                var key = string.IsNullOrEmpty(localFile.FolderPath)
                    ? localFile.PartName
                    : $"{localFile.FolderPath}/{localFile.PartName}";

                if (!remoteParts.ContainsKey(key))
                {
                    // Check if tracked - might already have a Part ID from a previous run
                    var tracked = trackingDb?.GetByPath(localFile.FilePath);
                    if (tracked != null && !string.IsNullOrEmpty(tracked.PartId))
                    {
                        // Verify the Part still exists in Printago (check if Part ID is in remoteParts)
                        bool partExistsInPrintago = remoteParts.Values.Any(list => list.Any(p => p.Id == tracked.PartId));

                        if (partExistsInPrintago)
                        {
                            Log($"  Skipping: {key} - already tracked to Part {tracked.PartId}", "DEBUG");
                            continue;
                        }
                        else
                        {
                            // Part was deleted from Printago (manually by user), need to re-upload
                            Log($"  Will upload (tracked Part {tracked.PartId} no longer exists in Printago): {key}", "DEBUG");
                            uploads.Add(localFile);
                        }
                    }
                    else
                    {
                        // New file
                        uploads.Add(localFile);
                        Log($"  Will upload (new): {key}", "DEBUG");
                    }
                }
                else
                {
                    // Check if changed - use the first (best) part for comparison
                    var remotePart = remoteParts[key].First();
                    if (await IsFileChanged(localFile, remotePart))
                    {
                        uploads.Add(localFile);
                        Log($"  Will upload (changed): {key}", "DEBUG");
                    }
                    else
                    {
                        Log($"  Skipping (up-to-date): {key}", "DEBUG");
                    }
                }
            }

            Log($"✓ Sync plan: {deletions.Count} deletions, {uploads.Count} uploads", "INFO");

            // Check if folders were deleted - if so, refresh cache and loop again
            if (foldersDeleted > 0)
            {
                Log($"Folders deleted: {foldersDeleted} - refreshing cache and looping...", "INFO");
                hasChanges = true;

                // Refresh cache to get updated folder/part structure
                await BuildInitialCache();
                continue; // Loop again
            }

            // No more folders to delete - proceed with queuing deletions and uploads
            hasChanges = false;
            Log($"========== SYNC PLAN COMPLETE ==========", "INFO");

            // Queue deletions
            foreach (var part in deletions)
            {
                deleteQueue.Enqueue(part);
            }

            // Queue uploads
            foreach (var file in uploads)
            {
                uploadQueue.Enqueue(file.FilePath);
            }
        }

        Log("========== ALL SYNC ITERATIONS COMPLETE ==========", "INFO");
    }

        /// <summary>
        /// Reconcile local files with tracking database.
        /// If file not in DB, try to match by hash to existing remote Part.
        /// Returns the number of folders deleted.
        /// </summary>
        private async Task<int> ReconcileWithTrackingDb()
        {
            if (trackingDb == null)
                return 0;

            Log("Reconciling files with tracking database...", "INFO");
            int matched = 0, added = 0, updated = 0;

            foreach (var localFile in localFiles.Values)
            {
                // Compute hash if not already done
                if (string.IsNullOrEmpty(localFile.FileHash))
                {
                    localFile.FileHash = await ComputeFileHash(localFile.FilePath);
                }

                // Check if file is already tracked by path
                var trackedByPath = trackingDb.GetByPath(localFile.FilePath);

                if (trackedByPath != null)
                {
                    // File is tracked - check if hash changed
                    if (trackedByPath.FileHash != localFile.FileHash)
                    {
                        Log($"File content changed: {localFile.PartName} (preserving Part ID)", "INFO");
                        trackingDb.UpdateHash(localFile.FilePath, localFile.FileHash);
                        updated++;
                    }
                    continue;
                }

                // Not tracked by path - check if file was moved (match by hash)
                var trackedByHash = trackingDb.GetByHash(localFile.FileHash);

                if (trackedByHash != null && trackedByHash.FilePath != localFile.FilePath)
                {
                    // File was moved! Update path in tracking DB (preserves Part ID)
                    Log($"File move detected: {trackedByHash.PartName} moved from {trackedByHash.FilePath} to {localFile.FilePath}", "INFO");
                    trackingDb.UpdatePath(trackedByHash.FilePath, localFile.FilePath);

                    // Also update the Part's folder location in Printago
                    Log($"Updating Part {trackedByHash.PartId} folder to '{localFile.FolderPath}'...", "INFO");
                    try
                    {
                        await UpdatePartFolder(trackedByHash.PartId, localFile.FolderPath);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to update Part {trackedByHash.PartId} folder: {ex.Message}", "ERROR");
                    }

                    matched++;
                    continue;
                }

                // Not tracked at all - try to match to existing remote Part by hash
                var key = string.IsNullOrEmpty(localFile.FolderPath)
                    ? localFile.PartName
                    : $"{localFile.FolderPath}/{localFile.PartName}";

                // First try exact key match
                if (remoteParts.TryGetValue(key, out var remotePartsList) && remotePartsList.Any(p => p.FileHash == localFile.FileHash))
                {
                    var remotePart = remotePartsList.First(p => p.FileHash == localFile.FileHash);
                    // Check if folder location matches
                    if (remotePart.FolderPath != localFile.FolderPath)
                    {
                        // Same file, different folder - it was moved!
                        Log($"Folder mismatch detected: {localFile.PartName} is in '{localFile.FolderPath}' locally but '{remotePart.FolderPath}' in Printago", "INFO");

                        // Update Part folder in Printago
                        Log($"Updating Part {remotePart.Id} folder to '{localFile.FolderPath}'...", "INFO");
                        try
                        {
                            await UpdatePartFolder(remotePart.Id, localFile.FolderPath);
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to update Part {remotePart.Id} folder: {ex.Message}", "ERROR");
                        }
                    }

                    // Match found! Add to tracking DB
                    Log($"Matched {localFile.PartName} to existing remote Part {remotePart.Id}", "INFO");
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

                // No exact key match - check if file exists in different folder (match by hash AND name)
                // Skip Parts without hashes (thumbnail processing not complete)
                var matchByHash = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.FileHash) &&
                    !string.IsNullOrEmpty(localFile.FileHash) &&
                    p.FileHash == localFile.FileHash &&
                    p.Name == localFile.PartName);
                if (matchByHash != null)
                {
                    // Found same file in different folder!
                    Log($"File found in different folder: {localFile.PartName} is in '{localFile.FolderPath}' locally but '{matchByHash.FolderPath}' in Printago", "INFO");

                    // Update Part folder in Printago
                    Log($"Updating Part {matchByHash.Id} folder to '{localFile.FolderPath}'...", "INFO");
                    try
                    {
                        await UpdatePartFolder(matchByHash.Id, localFile.FolderPath);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to update Part {matchByHash.Id} folder: {ex.Message}", "ERROR");
                    }

                    // Add to tracking DB
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

            Log($"Reconciliation complete: {matched} moves detected, {updated} updates, {added} matched to remote", "INFO");

            // Clean up empty folders that don't match local structure
            int foldersDeleted = await CleanupEmptyFolders();
            return foldersDeleted;
        }

        private async Task<bool> IsFileChanged(LocalFileInfo localFile, PartCache remotePart)
        {
            try
            {
                // Compute local file hash
                if (string.IsNullOrEmpty(localFile.FileHash))
                {
                    localFile.FileHash = await ComputeFileHash(localFile.FilePath);
                }

                // Compare hashes
                return localFile.FileHash != remotePart.FileHash;
            }
            catch
            {
                return true; // Assume changed if can't compare
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

                Log($"Deleted: {key} (not found locally)", "INFO");
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

                // Get or create the folder for the new path
                var newFolderId = await GetOrCreateFolder(newFolderPath);

                if (newFolderId == null)
                {
                    Log($"Cannot move Part {partId} - failed to get/create folder '{newFolderPath}'", "ERROR");
                    return;
                }

                // Update the Part's folderId
                var updateBody = new
                {
                    folderId = newFolderId
                };

                var request = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}/v1/parts/{partId}")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(updateBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Get the Part name for better logging
                    var partName = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p => p.Id == partId)?.Name ?? partId;
                    Log($"✓ MOVED: '{partName}' → '{newFolderPath}' (Part ID: {partId})", "MOVE");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Failed to move Part {partId}: HTTP {response.StatusCode} - {errorBody.Substring(0, Math.Min(200, errorBody.Length))}", "ERROR");
                }
            }
            catch (Exception ex)
            {
                Log($"Error updating Part {partId} folder: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// Updates an existing Part's file without losing metadata/settings.
        /// Uses PATCH to update only the file, preserving all other Part properties.
        /// </summary>
        private async Task<bool> UpdatePartFile(string partId, string filePath, string storagePath)
        {
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');

                // Update the Part's fileUris (this preserves all other metadata)
                var updateBody = new
                {
                    fileUris = new[] { storagePath }
                };

                var request = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}/v1/parts/{partId}")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(updateBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await SendApiRequestAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log($"✓ Updated Part {partId} with new file (metadata preserved)", "SUCCESS");
                    return true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log($"Failed to update Part {partId} file: HTTP {response.StatusCode} - {errorBody.Substring(0, Math.Min(200, errorBody.Length))}", "ERROR");
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

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLower();
            if (ext == ".3mf" || ext == ".stl")
            {
                // Debounce: ignore duplicate events within DEBOUNCE_MS
                var now = DateTime.UtcNow;
                if (lastEventTime.TryGetValue(e.FullPath, out var lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < DEBOUNCE_MS)
                    {
                        Log($"Debounced duplicate event for: {Path.GetFileName(e.FullPath)}", "DEBUG");
                        return;
                    }
                }
                lastEventTime[e.FullPath] = now;

                // Update local cache
                AddLocalFile(e.FullPath);

                // Check if this file already exists with same hash (avoid duplicates on copy operations)
                try
                {
                    var fileInfo = new FileInfo(e.FullPath);
                    var relativePath = Path.GetRelativePath(Config.WatchPath, e.FullPath);
                    var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                    var partName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    var fileHash = await ComputeFileHash(e.FullPath);

                    // Check tracking DB first
                    var tracked = trackingDb?.GetByHash(fileHash);
                    if (tracked != null)
                    {
                        // File with same hash already tracked - this is a copy, update tracking for new path
                        Log($"Detected file copy: {partName} (same as {tracked.PartName}) - updating Part location", "INFO");

                        // Update tracking DB with new path
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

                        // Update Part folder in Printago
                        await UpdatePartFolder(tracked.PartId, folderPath);
                        return; // Don't queue for upload
                    }

                    // Also check if Part exists in remote with same hash
                    var existingByHash = remoteParts.Values.SelectMany(list => list).FirstOrDefault(p =>
                        !string.IsNullOrEmpty(p.FileHash) &&
                        p.FileHash == fileHash &&
                        p.Name == partName);

                    if (existingByHash != null)
                    {
                        Log($"Detected file copy: {partName} matches existing Part {existingByHash.Id} - updating location", "INFO");

                        // Track this file
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

                        // Update Part folder in Printago
                        await UpdatePartFolder(existingByHash.Id, folderPath);
                        return; // Don't queue for upload
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error checking for duplicate on file change: {ex.Message}", "WARN");
                    // Fall through to queue for upload
                }

                // Queue for upload (new file or changed file) - but only if not already queued
                if (filesInUploadQueue.TryAdd(e.FullPath, true))
                {
                    uploadQueue.Enqueue(e.FullPath);
                    Log($"Detected change: {Path.GetFileName(e.FullPath)}", "INFO");
                }
                else
                {
                    Log($"Skipped queueing (already in queue): {Path.GetFileName(e.FullPath)}", "DEBUG");
                }
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLower();
            if (ext == ".3mf" || ext == ".stl")
            {
                var relativePath = Path.GetRelativePath(Config.WatchPath, e.FullPath);
                var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                var partName = Path.GetFileNameWithoutExtension(e.Name);

                var key = string.IsNullOrEmpty(folderPath)
                    ? partName
                    : $"{folderPath}/{partName}";

                // Remove from local cache
                localFiles.TryRemove(key, out _);

                // Get the Part's current hash before potentially deleting tracking
                var tracked = trackingDb?.GetByPath(e.FullPath);
                var oldHash = tracked?.FileHash ?? "";

                // Check if this Part exists in remote
                if (remoteParts.TryGetValue(key, out var remotePartList) && remotePartList.Any())
                {
                    var remotePart = remotePartList.First(); // Use the first one for deletion tracking
                    // Don't immediately delete - add to pending deletions with grace period
                    // This handles atomic save operations (delete + create pattern used by Bambu Studio)
                    pendingDeletions[e.FullPath] = (remotePart, DateTime.UtcNow, oldHash);
                    Log($"Detected deletion: {e.Name} (grace period: {DELETION_GRACE_PERIOD_MS}ms)", "INFO");

                    // Schedule delayed deletion check
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(DELETION_GRACE_PERIOD_MS);

                        // Check if file reappeared (atomic save pattern)
                        if (File.Exists(e.FullPath))
                        {
                            // File reappeared! This was an atomic save, not a real deletion
                            if (pendingDeletions.TryRemove(e.FullPath, out var pendingInfo))
                            {
                                Log($"File reappeared after deletion event: {e.Name} (atomic save detected)", "INFO");

                                // Check if content changed (hash comparison)
                                try
                                {
                                    var newHash = await ComputeFileHash(e.FullPath);
                                    if (newHash != pendingInfo.oldHash)
                                    {
                                        // File content changed - queue for update
                                        Log($"File content changed during atomic save: {e.Name}", "INFO");
                                        uploadQueue.Enqueue(e.FullPath);
                                    }
                                    else
                                    {
                                        Log($"File content unchanged after atomic save: {e.Name}", "DEBUG");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"Error checking file hash after atomic save: {ex.Message}", "WARN");
                                    // Queue for upload to be safe
                                    uploadQueue.Enqueue(e.FullPath);
                                }
                            }
                        }
                        else
                        {
                            // File did not reappear - this is a real deletion
                            if (pendingDeletions.TryRemove(e.FullPath, out var pendingInfo))
                            {
                                Log($"Confirmed deletion after grace period: {e.Name}", "INFO");

                                // Remove from tracking DB
                                trackingDb?.Delete(e.FullPath);

                                // Queue for deletion from remote
                                deleteQueue.Enqueue(pendingInfo.part);
                            }
                        }
                    });
                }
                else
                {
                    // Part doesn't exist in remote, just remove from tracking
                    trackingDb?.Delete(e.FullPath);
                    Log($"Detected deletion of untracked file: {e.Name}", "DEBUG");
                }
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            // Handle both file renames and folder renames (which affect all files inside)
            var ext = Path.GetExtension(e.FullPath).ToLower();
            if (ext == ".3mf" || ext == ".stl")
            {
                // File was renamed/moved
                Log($"Detected rename: {e.OldName} → {e.Name}", "INFO");

                // Remove old entry from local cache
                var oldRelativePath = Path.GetRelativePath(Config.WatchPath, e.OldFullPath);
                var oldFolderPath = Path.GetDirectoryName(oldRelativePath)?.Replace("\\", "/") ?? "";
                var oldPartName = Path.GetFileNameWithoutExtension(e.OldName);
                var oldKey = string.IsNullOrEmpty(oldFolderPath)
                    ? oldPartName
                    : $"{oldFolderPath}/{oldPartName}";
                localFiles.TryRemove(oldKey, out _);

                // Add new entry to local cache
                AddLocalFile(e.FullPath);

                // Trigger file change handler to update Part folder in Printago
                // This will use the hash-based tracking to detect the move
                OnFileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath) ?? "", Path.GetFileName(e.FullPath)));
            }
            else if (Directory.Exists(e.FullPath))
            {
                // Folder was renamed - trigger a full resync to handle all files in the folder
                Log($"Detected folder rename: {e.OldName} → {e.Name}", "INFO");
                Log("Triggering full rescan to handle folder rename...", "INFO");

                // Trigger a manual sync to rescan everything
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000); // Small delay to let FS settle
                    await TriggerSyncNow();
                });
            }
        }

        #endregion

        #region Delete Processing

        private async Task ProcessDeleteQueue(CancellationToken ct)
        {
            // Process deletions one at a time with 2-second delays
            while (!ct.IsCancellationRequested)
            {
                if (deleteQueue.TryDequeue(out var part))
                {
                    await DeletePart(part);
                }

                // Wait 2 seconds before processing next deletion
                await Task.Delay(2000, ct);
            }
        }

        #endregion

        #region Upload Processing

        private async Task ProcessUploadQueue(CancellationToken ct)
        {
            // Dequeue and start uploads every 2 seconds
            // Each upload will independently wait for a semaphore slot (max 10 concurrent)
            while (!ct.IsCancellationRequested)
            {
                if (uploadQueue.TryDequeue(out var filePath))
                {
                    // Start the upload task (it will wait for a slot internally)
                    _ = Task.Run(() => ProcessSingleUpload(filePath, ct), ct);
                }

                // Wait 2 seconds before dequeuing the next file
                await Task.Delay(2000, ct);
            }
        }

        private async Task ProcessSingleUpload(string filePath, CancellationToken ct)
        {
            // Wait for a semaphore slot (blocks if 10 uploads are already active)
            await uploadSemaphore.WaitAsync(ct);
            try
            {
                await Task.Delay(2000, ct); // File stability wait

                if (File.Exists(filePath))
                {
                    await UploadFile(filePath);
                }
            }
            finally
            {
                uploadSemaphore.Release();
                // Remove from queue tracking so file can be queued again if changed
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

            // Build the key early so we can use it for per-key locking
            var key = string.IsNullOrEmpty(folderPath)
                ? partName
                : $"{folderPath}/{partName}";

            // Get or create a lock for this specific key to prevent race conditions
            var keyLock = uploadKeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync();

            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');

                PartCache? existingPart = null;
                bool isUpdate = false;

                // Re-check cache after acquiring lock (another upload may have completed)
                if (remoteParts.TryGetValue(key, out var existingPartsList) && existingPartsList.Any())
                {
                    existingPart = existingPartsList.First(); // Use the first (best) one
                    progress.Status = "Checking for changes...";
                    progress.ProgressPercent = 5;

                    var localHash = await ComputeFileHash(filePath);

                    if (localHash == existingPart.FileHash)
                    {
                        progress.Status = "Already up-to-date";
                        progress.ProgressPercent = 100;
                        Log($"Skipped: {key} (already up-to-date)", "INFO");
                        await Task.Delay(1000);
                        activeUploads.TryRemove(filePath, out _);
                        return;
                    }

                    // File changed - we'll update the existing Part (preserves metadata)
                    isUpdate = true;
                    Log($"File changed: {key} - updating existing Part (preserving metadata)", "INFO");
                }

                // Create folder - always use GetOrCreateFolder which will prepend "Local Folder Sync"
                progress.Status = "Creating folders...";
                progress.ProgressPercent = 10;
                string? folderId = await GetOrCreateFolder(folderPath);

                // Read file
                progress.Status = "Reading file...";
                progress.ProgressPercent = 15;
                var fileBytes = await File.ReadAllBytesAsync(filePath);

                // Get signed URL
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

                // Upload to cloud
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
                    // Update existing Part (preserves all metadata)
                    progress.Status = "Updating part...";
                    progress.ProgressPercent = 80;

                    bool updateSuccess = await UpdatePartFile(existingPart.Id, filePath, signedUrlResponse.Value.storagePath);

                    if (updateSuccess)
                    {
                        partId = existingPart.Id;

                        // Update cache with new hash - replace with single-item list
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

                        // Update tracking database
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
                        Log($"Updated: {key} (Part ID: {partId}) - metadata preserved", "SUCCESS");
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
                    // Create new Part
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
                        // Get the Part ID from response
                        var partResponseJson = await partResponse.Content.ReadAsStringAsync();
                        var createdPart = JsonConvert.DeserializeAnonymousType(partResponseJson, new { id = "" });
                        partId = createdPart?.id ?? "";

                        // Update cache - replace with single-item list
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

                        // Track in database for future reconciliation
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
            // If folderPath is empty, return the root sync folder ID
            if (string.IsNullOrEmpty(folderPath))
            {
                await EnsureRootSyncFolder();
                return rootSyncFolderId;
            }

            // Prepend "Local Folder Sync" to the folder path
            var fullPath = $"{ROOT_SYNC_FOLDER}/{folderPath}";

            // Check if folder exists with full path (quick check without lock)
            if (remoteFolders.TryGetValue(fullPath, out var cached))
                return cached.Id;

            // ALSO check if folder exists at root without prefix (legacy folders before "Local Folder Sync" feature)
            // If it does, return that folder ID instead of creating duplicates
            if (remoteFolders.TryGetValue(folderPath, out var legacyFolder))
            {
                Log($"Using existing root-level folder '{folderPath}' instead of creating under '{ROOT_SYNC_FOLDER}'", "INFO");
                return legacyFolder.Id;
            }

            // Acquire lock to prevent duplicate folder creation from concurrent uploads
            await folderCreationLock.WaitAsync();
            try
            {
                // Double-check if folder was created by another thread while waiting for lock
                if (remoteFolders.TryGetValue(fullPath, out var recheck))
                    return recheck.Id;

                var apiUrl = Config.ApiUrl.TrimEnd('/');

                // Ensure root sync folder exists first
                await EnsureRootSyncFolder();

                // Start with root sync folder as parent
                string? parentId = rootSyncFolderId;
                string currentPath = ROOT_SYNC_FOLDER;

            // Now iterate through the subfolders
            var subParts = folderPath.Split('/');
            foreach (var folderName in subParts)
            {
                currentPath = $"{currentPath}/{folderName}";

                if (remoteFolders.TryGetValue(currentPath, out var existing))
                {
                    parentId = existing.Id;
                    continue;
                }

                // Create folder
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
                        Log($"Failed to create folder {currentPath} - empty response", "ERROR");
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

        /// <summary>
        /// Cleans up empty folders in Printago that don't match local folder structure.
        /// Returns the number of folders successfully deleted.
        /// </summary>
        private async Task<int> CleanupEmptyFolders()
        {
            int deletedCount = 0;
            try
            {
                Log("Starting folder cleanup - checking for empty folders not matching local structure...", "INFO");

                // Get all folders from Printago
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var folders = await FetchAllFolders(apiUrl);
                var parts = await FetchAllParts(apiUrl);

                // Build a set of folder IDs that have Parts
                var foldersWithParts = new HashSet<string>();
                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part.folderId))
                    {
                        foldersWithParts.Add(part.folderId);
                    }
                }

                // Build a set of folder IDs that have child folders
                var foldersWithChildren = new HashSet<string>();
                foreach (var folder in folders)
                {
                    if (!string.IsNullOrEmpty(folder.parentId))
                    {
                        foldersWithChildren.Add(folder.parentId);
                    }
                }

                // Build a set of local folder paths (normalized)
                var localFolderPaths = new HashSet<string>();
                foreach (var localFile in localFiles.Values)
                {
                    if (!string.IsNullOrEmpty(localFile.FolderPath))
                    {
                        // Add all parent paths
                        var parts_path = localFile.FolderPath.Split('/');
                        var currentPath = "";
                        foreach (var part_name in parts_path)
                        {
                            currentPath = string.IsNullOrEmpty(currentPath) ? part_name : $"{currentPath}/{part_name}";
                            localFolderPaths.Add(currentPath);
                        }
                    }
                }

                Log($"Found {localFolderPaths.Count} unique local folder paths", "DEBUG");

                // Find folders to delete (empty AND not matching local structure)
                var foldersToDelete = new List<FolderDto>();

                foreach (var folder in folders)
                {
                    // Skip root "Local Folder Sync" folder
                    if (folder.name == ROOT_SYNC_FOLDER && folder.parentId == null)
                        continue;

                    // Check if folder is empty (no Parts and no child folders)
                    bool isEmpty = !foldersWithParts.Contains(folder.id) && !foldersWithChildren.Contains(folder.id);

                    if (isEmpty)
                    {
                        // Reconstruct folder path
                        var folderPath = ReconstructFolderPath(folder.id, folders);

                        // Normalize: remove "Local Folder Sync/" prefix if present
                        var normalizedPath = folderPath;
                        if (folderPath.StartsWith($"{ROOT_SYNC_FOLDER}/"))
                        {
                            normalizedPath = folderPath.Substring(ROOT_SYNC_FOLDER.Length + 1);
                        }

                        // Check if this path exists locally
                        bool existsLocally = localFolderPaths.Contains(normalizedPath);

                        if (!existsLocally)
                        {
                            foldersToDelete.Add(folder);
                            Log($"  Will delete empty folder: '{folderPath}' (not in local structure)", "DEBUG");
                        }
                    }
                }

                if (foldersToDelete.Count > 0)
                {
                    Log($"Found {foldersToDelete.Count} empty folders to delete", "INFO");

                    // Delete folders (from deepest to shallowest to avoid parent-child conflicts)
                    var sortedFolders = foldersToDelete.OrderByDescending(f => ReconstructFolderPath(f.id, folders).Count(c => c == '/')).ToList();

                    foreach (var folder in sortedFolders)
                    {
                        try
                        {
                            var folderPath = ReconstructFolderPath(folder.id, folders);

                            // Use the correct bulk delete endpoint with request body
                            var deleteBody = new
                            {
                                folderIds = new[] { folder.id },
                                type = "part" // All folder sync folders are type "part"
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
                                Log($"Failed to delete folder '{folderPath}': HTTP {response.StatusCode} - {errorBody.Substring(0, Math.Min(100, errorBody.Length))}", "ERROR");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error deleting folder {folder.name}: {ex.Message}", "ERROR");
                        }
                    }
                }
                else
                {
                    Log("No empty folders to clean up", "INFO");
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

            // Add to recent logs queue (for UI display)
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            recentLogs.Enqueue(logEntry);

            // Keep only last MAX_RECENT_LOGS entries
            while (recentLogs.Count > MAX_RECENT_LOGS)
            {
                recentLogs.TryDequeue(out _);
            }

            // Also write to file in AppData (writable location)
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
