using System;

namespace PrintagoFolderWatch
{
    public class PartDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string? type { get; set; }  // File extension (stl, 3mf, etc.)
        public string? folderId { get; set; }
        public string[] fileHashes { get; set; } = new string[0];
        public DateTime updatedAt { get; set; }
    }
}
