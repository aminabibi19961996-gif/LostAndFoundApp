using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Single-row table storing the activity log retention period.
    /// Admin and SuperAdmin can update the value.
    /// A daily background service reads this setting and purges older records.
    /// </summary>
    public class LogRetentionSettings
    {
        public int Id { get; set; }

        /// <summary>
        /// Number of days to retain activity log records.
        /// Allowed values: 30, 60, or 90. Logs older than this are automatically purged.
        /// </summary>
        [Required]
        [Display(Name = "Retention Period (days)")]
        public int RetentionDays { get; set; } = 30;

        /// <summary>
        /// UTC timestamp of the last successful purge run by the background service.
        /// </summary>
        public DateTime? LastPurgedAt { get; set; }

        /// <summary>
        /// Number of records deleted during the last purge run.
        /// </summary>
        public int? LastPurgedCount { get; set; }
    }
}
