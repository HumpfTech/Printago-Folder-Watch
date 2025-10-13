using System;

namespace PrintagoFolderWatch
{
    public class MoveOperation
    {
        public string PartId { get; set; } = "";
        public string PartName { get; set; } = "";
        public string FromFolder { get; set; } = "";
        public string ToFolder { get; set; } = "";
        public DateTime QueuedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Pending";
    }
}
