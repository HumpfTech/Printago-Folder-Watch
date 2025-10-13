using System;

namespace PrintagoFolderWatch
{
    public class PartCache
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? FolderId { get; set; }
        public string FolderPath { get; set; } = ""; // Reconstructed folder path
        public string FileHash { get; set; } = ""; // SHA256 from fileHashes[0]
        public DateTime UpdatedAt { get; set; }
    }
}
