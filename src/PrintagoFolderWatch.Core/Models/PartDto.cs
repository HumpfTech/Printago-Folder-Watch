using System;

namespace PrintagoFolderWatch.Core.Models
{
    public class PartDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string? folderId { get; set; }
        public string[] fileHashes { get; set; } = new string[0];
        public DateTime updatedAt { get; set; }
    }
}
