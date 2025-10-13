using System;

namespace PrintagoFolderWatch
{
    public class FolderDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string? parentId { get; set; }
    }
}
