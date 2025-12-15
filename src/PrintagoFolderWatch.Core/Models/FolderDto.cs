using System;

namespace PrintagoFolderWatch.Core.Models
{
    public class FolderDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string? parentId { get; set; }
    }
}
