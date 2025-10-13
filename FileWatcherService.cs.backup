using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PrintagoFolderWatch
{
    public class FileWatcherService : IDisposable
    {
        public Config Config { get; private set; }
        public event Action<string, string>? OnLog;

        private FileSystemWatcher? watcher;
        private readonly ConcurrentQueue<string> uploadQueue = new();
        private readonly HttpClient httpClient = new();
        private CancellationTokenSource? cts;
        private bool isRunning = false;
        private readonly ConcurrentDictionary<string, string> folderCache = new();
        private int syncedFilesCount = 0;
        private int foldersCreatedCount = 0;
        private readonly ConcurrentDictionary<string, UploadProgress> activeUploads = new();
        private const int MAX_PARALLEL_UPLOADS = 10; // 10 parallel uploads
        private readonly SemaphoreSlim folderApiSemaphore = new SemaphoreSlim(1, 1); // Only 1 folder API call at a time
        private readonly SemaphoreSlim uploadSemaphore = new SemaphoreSlim(MAX_PARALLEL_UPLOADS, MAX_PARALLEL_UPLOADS); // Limit concurrent uploads

        // Public properties for status tracking
        public int UploadQueueCount => uploadQueue.Count;
        public int FoldersCreatedCount => folderCache.Count; // Total folders (created + existing)
        public int SyncedFilesCount => syncedFilesCount;
        public List<UploadProgress> GetActiveUploads() => activeUploads.Values.ToList();

        public FileWatcherService()
        {
            Config = Config.Load();
        }

        public bool Start()
        {
            if (isRunning || !Config.IsValid())
                return false;

            try
            {
                isRunning = true;
                cts = new CancellationTokenSource();

                watcher = new FileSystemWatcher(Config.WatchPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };

                watcher.Created += OnFileChanged;
                watcher.Changed += OnFileChanged;

                // Start single queue processor that respects rate limits
                Task.Run(() => ProcessUploadQueue(cts.Token));

                // Upload existing files
                Task.Run(() => UploadExistingFiles());

                // Start one-way sync (delete remote parts not found locally)
                Task.Run(() => SyncPartsLoop(cts.Token));

                Log("Started watching: " + Config.WatchPath, "SUCCESS");
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
            await SyncParts();
        }

        public System.Collections.Generic.List<string> GetQueueItems()
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

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (File.Exists(e.FullPath))
            {
                uploadQueue.Enqueue(e.FullPath);
                Log($"Detected: {Path.GetFileName(e.FullPath)}", "INFO");
            }
        }

        private void UploadExistingFiles()
        {
            try
            {
                var files = Directory.GetFiles(Config.WatchPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    uploadQueue.Enqueue(file);
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning existing files: {ex.Message}", "ERROR");
            }
        }

        private async Task ProcessUploadQueue(CancellationToken ct)
        {
            // Start with 10 initial uploads
            for (int i = 0; i < MAX_PARALLEL_UPLOADS; i++)
            {
                if (uploadQueue.TryDequeue(out var initialFile))
                {
                    _ = Task.Run(() => ProcessSingleUpload(initialFile, ct), ct);
                }
            }

            // Then add 1 new upload every 2 seconds
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct); // 2 second delay = 30 uploads/min

                if (uploadQueue.TryDequeue(out var filePath))
                {
                    _ = Task.Run(() => ProcessSingleUpload(filePath, ct), ct);
                }
            }
        }

        private async Task ProcessSingleUpload(string filePath, CancellationToken ct)
        {
            // Wait for semaphore slot (max 10 concurrent)
            await uploadSemaphore.WaitAsync(ct);
            try
            {
                // Wait for file to be stable
                await Task.Delay(2000, ct);

                if (File.Exists(filePath))
                {
                    await UploadFileWithProgress(filePath);
                }
            }
            finally
            {
                uploadSemaphore.Release();
            }
        }

        private async Task<string?> GetOrCreateFolder(string folderPath, string apiUrl)
        {
            if (string.IsNullOrEmpty(folderPath) || folderPath == ".")
                return null;

            if (folderCache.TryGetValue(folderPath, out var cachedId))
                return cachedId;

            // Use semaphore to prevent concurrent folder API calls (avoid rate limiting)
            await folderApiSemaphore.WaitAsync();
            try
            {
                // Check cache again after acquiring lock
                if (folderCache.TryGetValue(folderPath, out var cachedId2))
                    return cachedId2;

                var folderParts = folderPath.Split('/', '\\').Where(p => !string.IsNullOrEmpty(p)).ToArray();
                string? parentId = null;
                string currentPath = "";

                foreach (var folderName in folderParts)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? folderName : $"{currentPath}/{folderName}";

                    if (folderCache.TryGetValue(currentPath, out var existingId))
                    {
                        parentId = existingId;
                        continue;
                    }

                    try
                    {
                        // Check if folder exists
                        var getFoldersRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/v1/folders");
                        getFoldersRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                        getFoldersRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                        var getFoldersResponse = await httpClient.SendAsync(getFoldersRequest);
                        var foldersJson = await getFoldersResponse.Content.ReadAsStringAsync();

                        // Check if response is HTML (error page)
                        if (foldersJson.TrimStart().StartsWith("<"))
                        {
                            Log($"Folders API returned HTML error for {currentPath}, skipping folder creation", "ERROR");
                            return null;
                        }

                        var folders = JsonConvert.DeserializeAnonymousType(foldersJson, new[]
                        {
                            new { id = "", name = "", parentId = (string?)null }
                        });

                        var existingFolder = folders?.FirstOrDefault(f =>
                            f.name == folderName && f.parentId == parentId);

                        if (existingFolder != null)
                        {
                            folderCache[currentPath] = existingFolder.id;
                            parentId = existingFolder.id;
                        }
                        else
                        {
                            // Create folder
                            var createFolderBody = new
                            {
                                name = folderName,
                                type = "part",
                                parentId = parentId
                            };

                            var createFolderRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/folders")
                            {
                                Content = new StringContent(JsonConvert.SerializeObject(createFolderBody), Encoding.UTF8, "application/json")
                            };
                            createFolderRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                            createFolderRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                            var createFolderResponse = await httpClient.SendAsync(createFolderRequest);
                            var createdFolderJson = await createFolderResponse.Content.ReadAsStringAsync();
                            var createdFolder = JsonConvert.DeserializeAnonymousType(createdFolderJson, new { id = "" });

                            if (createdFolder != null)
                            {
                                folderCache[currentPath] = createdFolder.id;
                                parentId = createdFolder.id;
                                Log($"Created folder: {currentPath}", "INFO");
                                System.Threading.Interlocked.Increment(ref foldersCreatedCount);
                            }
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
                folderApiSemaphore.Release();
            }
        }

        private async Task UploadFileWithProgress(string filePath)
        {
            var relativePath = Path.GetRelativePath(Config.WatchPath, filePath);
            var fileName = Path.GetFileName(filePath);

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

            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');

                // Check if part already exists (for 3D files only)
                var fileExt = Path.GetExtension(filePath).ToLower();
                if (fileExt == ".3mf" || fileExt == ".stl")
                {
                    progress.Status = "Checking if exists...";
                    progress.ProgressPercent = 5;

                    var partName = Path.GetFileNameWithoutExtension(fileName);

                    var checkRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/v1/parts?limit=10000");
                    checkRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                    checkRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                    var checkResponse = await httpClient.SendAsync(checkRequest);
                    if (checkResponse.IsSuccessStatusCode)
                    {
                        var partsJson = await checkResponse.Content.ReadAsStringAsync();
                        var existingParts = JsonConvert.DeserializeAnonymousType(partsJson, new[]
                        {
                            new { id = "", name = "" }
                        });

                        if (existingParts?.Any(p => p.name == partName) == true)
                        {
                            progress.Status = "Already exists - skipped";
                            progress.ProgressPercent = 100;
                            Log($"Skipped: {partName} (already exists)", "INFO");
                            await Task.Delay(1000); // Keep visible for a moment
                            activeUploads.TryRemove(filePath, out _);
                            return;
                        }
                    }
                }

                progress.Status = "Reading file...";
                progress.ProgressPercent = 10;
                var fileBytes = await File.ReadAllBytesAsync(filePath);

                var cloudPath = relativePath.Replace("\\", "/");

                progress.Status = "Getting signed URL...";
                progress.ProgressPercent = 15;

                // Step 1: Get signed URL
                var requestBody = new
                {
                    filenames = new[] { cloudPath }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/storage/signed-upload-urls")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                // Check if response is HTML (rate limit error) or 429 status
                if (responseJson.TrimStart().StartsWith("<") || !response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // Check for Retry-After header
                        int retryAfterSeconds = 5; // Default
                        if (response.Headers.TryGetValues("Retry-After", out var retryValues))
                        {
                            var retryValue = retryValues.FirstOrDefault();
                            if (int.TryParse(retryValue, out var seconds))
                            {
                                retryAfterSeconds = seconds;
                            }
                        }

                        progress.Status = $"Rate limited - waiting {retryAfterSeconds}s...";
                        Log($"Rate limited (429) on signed URL for {cloudPath}, waiting {retryAfterSeconds} seconds before retry", "ERROR");

                        // Wait the specified time before retrying
                        await Task.Delay(retryAfterSeconds * 1000);

                        // Re-queue to retry
                        uploadQueue.Enqueue(filePath);
                        activeUploads.TryRemove(filePath, out _);
                        return;
                    }
                    else if (responseJson.TrimStart().StartsWith("<"))
                    {
                        progress.Status = "API error - retrying...";
                        Log($"API returned HTML error for {cloudPath}, will retry in 2 seconds", "ERROR");

                        // Wait 2 seconds before retry
                        await Task.Delay(2000);

                        uploadQueue.Enqueue(filePath);
                        activeUploads.TryRemove(filePath, out _);
                        return;
                    }
                }

                var signedUrlResponse = JsonConvert.DeserializeAnonymousType(responseJson, new
                {
                    signedUrls = new[] { new { uploadUrl = "", path = "" } }
                });

                if (signedUrlResponse?.signedUrls == null || !signedUrlResponse.signedUrls.Any())
                {
                    progress.Status = "Failed - No signed URL";
                    Log($"Failed: {cloudPath} - No signed URL", "ERROR");
                    activeUploads.TryRemove(filePath, out _);
                    return;
                }

                var uploadUrl = signedUrlResponse.signedUrls[0].uploadUrl;
                var storagePath = signedUrlResponse.signedUrls[0].path;

                progress.Status = "Uploading to cloud...";
                progress.ProgressPercent = 30;

                // Step 2: Upload to signed URL
                var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = new ByteArrayContent(fileBytes)
                };

                var uploadResponse = await httpClient.SendAsync(uploadRequest);

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    progress.Status = $"Failed - {uploadResponse.StatusCode}";
                    Log($"Failed: {cloudPath} - Status {uploadResponse.StatusCode}", "ERROR");
                    activeUploads.TryRemove(filePath, out _);
                    return;
                }

                progress.Status = "Upload complete, creating part...";
                progress.ProgressPercent = 70;

                // Step 3: Create part in Printago (only for 3D model files)
                if (fileExt == ".3mf" || fileExt == ".stl")
                {
                    var partName = Path.GetFileNameWithoutExtension(fileName);
                    var partType = fileExt == ".3mf" ? "3mf" : "stl";

                    // Get or create folder structure
                    var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/");
                    var folderId = await GetOrCreateFolder(folderPath ?? "", apiUrl);

                    progress.ProgressPercent = 85;

                    var partBody = new
                    {
                        name = partName,
                        type = partType,
                        description = "",
                        fileUris = new[] { storagePath },
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

                    var partResponse = await httpClient.SendAsync(partRequest);
                    if (partResponse.IsSuccessStatusCode)
                    {
                        progress.Status = "Complete!";
                        progress.ProgressPercent = 100;
                        var folderInfo = !string.IsNullOrEmpty(folderId) ? $" in folder {folderPath}" : "";
                        Log($"Created part: {partName}{folderInfo}", "SUCCESS");
                        System.Threading.Interlocked.Increment(ref syncedFilesCount);
                    }
                    else
                    {
                        var errorContent = await partResponse.Content.ReadAsStringAsync();
                        progress.Status = $"Failed to create part - {partResponse.StatusCode}";
                        Log($"Failed to create part: {partName} - {partResponse.StatusCode}", "ERROR");
                    }
                }
                else
                {
                    progress.Status = "Complete!";
                    progress.ProgressPercent = 100;
                    Log($"Uploaded: {cloudPath}", "SUCCESS");
                }

                await Task.Delay(2000); // Keep in list for 2 seconds so user can see completion
            }
            catch (Exception ex)
            {
                progress.Status = $"Error: {ex.Message}";
                progress.ProgressPercent = 0;
                Log($"Failed: {Path.GetFileName(filePath)} - {ex.Message}", "ERROR");
            }
            finally
            {
                activeUploads.TryRemove(filePath, out _);
            }
        }

        private async Task UploadFile(string filePath)
        {
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var relativePath = Path.GetRelativePath(Config.WatchPath, filePath);
                var cloudPath = relativePath.Replace("\\", "/");
                var fileName = Path.GetFileName(filePath);
                var fileExt = Path.GetExtension(filePath).ToLower();

                // Step 1: Get signed URL
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var requestBody = new
                {
                    filenames = new[] { cloudPath }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/v1/storage/signed-upload-urls")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                request.Headers.Add("x-printago-storeid", Config.StoreId);

                var response = await httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();
                var signedUrlResponse = JsonConvert.DeserializeAnonymousType(responseJson, new
                {
                    signedUrls = new[] { new { uploadUrl = "", path = "" } }
                });

                if (signedUrlResponse?.signedUrls == null || !signedUrlResponse.signedUrls.Any())
                {
                    Log($"Failed: {cloudPath} - No signed URL", "ERROR");
                    return;
                }

                var uploadUrl = signedUrlResponse.signedUrls[0].uploadUrl;
                var storagePath = signedUrlResponse.signedUrls[0].path;

                // Step 2: Upload to signed URL
                var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = new ByteArrayContent(fileBytes)
                };

                var uploadResponse = await httpClient.SendAsync(uploadRequest);

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    Log($"Failed: {cloudPath} - Status {uploadResponse.StatusCode}", "ERROR");
                    return;
                }

                // Step 3: Create part in Printago (only for 3D model files)
                if (fileExt == ".3mf" || fileExt == ".stl")
                {
                    var partName = Path.GetFileNameWithoutExtension(fileName);
                    var partType = fileExt == ".3mf" ? "3mf" : "stl";

                    // Get or create folder structure
                    var folderPath = Path.GetDirectoryName(relativePath)?.Replace("\\", "/");
                    var folderId = await GetOrCreateFolder(folderPath ?? "", apiUrl);

                    var partBody = new
                    {
                        name = partName,
                        type = partType,
                        description = "",
                        fileUris = new[] { storagePath },
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

                    var partResponse = await httpClient.SendAsync(partRequest);
                    if (partResponse.IsSuccessStatusCode)
                    {
                        var folderInfo = !string.IsNullOrEmpty(folderId) ? $" in folder {folderPath}" : "";
                        Log($"Created part: {partName}{folderInfo}", "SUCCESS");
                        System.Threading.Interlocked.Increment(ref syncedFilesCount);
                    }
                    else
                    {
                        var errorContent = await partResponse.Content.ReadAsStringAsync();
                        Log($"Failed to create part: {partName} - {partResponse.StatusCode}", "ERROR");
                    }
                }
                else
                {
                    Log($"Uploaded: {cloudPath}", "SUCCESS");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed: {Path.GetFileName(filePath)} - {ex.Message}", "ERROR");
            }
        }

        private async Task SyncPartsLoop(CancellationToken ct)
        {
            // Wait 30 seconds after startup before first sync
            await Task.Delay(30000, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await SyncParts();
                    // Sync every 5 minutes
                    await Task.Delay(300000, ct);
                }
                catch (Exception ex)
                {
                    Log($"Sync error: {ex.Message}", "ERROR");
                    await Task.Delay(60000, ct); // Wait 1 minute before retry
                }
            }
        }

        private async Task SyncParts()
        {
            try
            {
                var apiUrl = Config.ApiUrl.TrimEnd('/');
                var watchPath = Config.WatchPath.TrimEnd('\\', '/');

                // Get all local 3D files
                var localFiles = new HashSet<string>();
                ScanLocalFiles(watchPath, localFiles, watchPath);

                // Get all parts from Printago
                var getPartsRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/v1/parts?limit=10000");
                getPartsRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                getPartsRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                var partsResponse = await httpClient.SendAsync(getPartsRequest);
                var partsJson = await partsResponse.Content.ReadAsStringAsync();
                var parts = JsonConvert.DeserializeAnonymousType(partsJson, new[]
                {
                    new { id = "", name = "" }
                });

                if (parts == null)
                    return;

                // Delete parts that don't exist locally
                int deletedCount = 0;
                foreach (var part in parts)
                {
                    if (!localFiles.Contains(part.name))
                    {
                        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{apiUrl}/v1/parts/{part.id}");
                        deleteRequest.Headers.Add("authorization", $"ApiKey {Config.ApiKey}");
                        deleteRequest.Headers.Add("x-printago-storeid", Config.StoreId);

                        await httpClient.SendAsync(deleteRequest);
                        Log($"Deleted part: {part.name} (not found locally)", "INFO");
                        deletedCount++;

                        // Rate limiting
                        await Task.Delay(1000);
                    }
                }

                if (deletedCount > 0)
                {
                    Log($"Sync complete: deleted {deletedCount} parts", "SUCCESS");
                }
            }
            catch (Exception ex)
            {
                Log($"Sync error: {ex.Message}", "ERROR");
            }
        }

        private void ScanLocalFiles(string dir, HashSet<string> localFiles, string basePath)
        {
            try
            {
                var files = Directory.GetFiles(dir);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".3mf" || ext == ".stl")
                    {
                        var partName = Path.GetFileNameWithoutExtension(file);
                        localFiles.Add(partName);
                    }
                }

                var directories = Directory.GetDirectories(dir);
                foreach (var directory in directories)
                {
                    ScanLocalFiles(directory, localFiles, basePath);
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning directory: {ex.Message}", "ERROR");
            }
        }

        private void Log(string message, string level)
        {
            OnLog?.Invoke(message, level);
        }

        public void Dispose()
        {
            Stop();
            httpClient?.Dispose();
            cts?.Dispose();
        }
    }
}
