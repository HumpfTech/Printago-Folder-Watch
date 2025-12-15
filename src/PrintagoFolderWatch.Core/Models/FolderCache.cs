using System;

namespace PrintagoFolderWatch.Core.Models
{
    public class FolderCache
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }
        public string FolderPath { get; set; } = "";
    }
}
