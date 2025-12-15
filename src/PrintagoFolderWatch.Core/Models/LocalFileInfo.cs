using System;

namespace PrintagoFolderWatch.Core.Models
{
    public class LocalFileInfo
    {
        public string FilePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string PartName { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public string FileHash { get; set; } = "";
    }
}
