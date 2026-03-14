using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Single-row table storing the lost-and-found case record retention period.
    /// Admin and SuperAdmin can update the value.
    /// A daily background service reads this setting and purges older records.
    /// Master data tables (Items, Routes, Vehicles, etc.) are never affected.
    /// </summary>
    public class ItemRetentionSettings
    {
        public int Id { get; set; }

        /// <summary>
        /// Number of days to retain lost-and-found case records.
        /// Allowed values: 365 (1 year) or 730 (2 years).
        /// Records older than this are automatically purged.
        /// </summary>
        [Required]
        [Display(Name = "Retention Period (days)")]
        public int RetentionDays { get; set; } = 365;

        /// <summary>
        /// Timestamp of the last successful purge run by the background service.
        /// </summary>
        public DateTime? LastPurgedAt { get; set; }

        /// <summary>
        /// Number of records deleted during the last purge run.
        /// </summary>
        public int? LastPurgedCount { get; set; }
    }
}
