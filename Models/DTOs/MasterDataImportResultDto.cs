using System;

namespace PickNBook.Api.Models.DTOs
{
    public class MasterDataImportResultDto
    {
        public string EntityType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int RecordsRead { get; set; }
        public int RecordsInserted { get; set; }
        public int RecordsUpdated { get; set; }
        public int RecordsDeactivated { get; set; }
        public int RecordsFailed { get; set; }
        public long DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
