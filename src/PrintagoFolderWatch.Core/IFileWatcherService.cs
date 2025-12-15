using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrintagoFolderWatch.Core.Models;

namespace PrintagoFolderWatch.Core
{
    public interface IFileWatcherService
    {
        Config Config { get; }
        event Action<string, string>? OnLog;

        int UploadQueueCount { get; }
        int DeleteQueueCount { get; }
        int FoldersCreatedCount { get; }
        int SyncedFilesCount { get; }

        List<UploadProgress> GetActiveUploads();
        List<string> GetQueueItems();
        List<string> GetDeleteQueueItems();
        List<string> GetRecentLogs(int count);
        Task TriggerSyncNow();
    }
}
