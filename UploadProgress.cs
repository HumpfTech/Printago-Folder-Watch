using System;

namespace PrintagoFolderWatch
{
    public class UploadProgress
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public int ProgressPercent { get; set; } = 0;
        public string Status { get; set; } = "Waiting...";
        public DateTime StartTime { get; set; } = DateTime.Now;
        public long FileSizeBytes { get; set; } = 0;
    }
}
