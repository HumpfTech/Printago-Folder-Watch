using System;

namespace PrintagoFolderWatch.Core.Models
{
    public class PartCache
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? FolderId { get; set; }
        public string FolderPath { get; set; } = "";
        public string FileHash { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }
}
