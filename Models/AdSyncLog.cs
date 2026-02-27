using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Stores a history record for each AD sync operation (manual or scheduled).
    /// Provides visibility into when syncs ran and what happened.
    /// </summary>
    public class AdSyncLog
    {
        public int Id { get; set; }

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool Success { get; set; }

        public int UsersCreated { get; set; }
        public int UsersUpdated { get; set; }
        public int UsersDeactivated { get; set; }
        public int RolesUpdated { get; set; }

        [StringLength(50)]
        public string TriggerType { get; set; } = "Manual"; // "Manual" or "Scheduled"

        [StringLength(256)]
        public string? TriggeredBy { get; set; }

        [StringLength(2000)]
        public string? ErrorSummary { get; set; }
    }
}
