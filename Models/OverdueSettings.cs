using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Single-row table storing configurable overdue thresholds.
    /// SuperAdmin and Admin can update these values.
    /// Replaces hardcoded 7-day and 30-day constants throughout the application.
    /// </summary>
    public class OverdueSettings
    {
        public int Id { get; set; }

        /// <summary>
        /// Items in the system for this many days or more (and not resolved) are flagged as "Overdue".
        /// Previously hardcoded as 7. Shown on dashboards and Search.
        /// </summary>
        [Range(1, 365)]
        [Display(Name = "Short Overdue Threshold (days)")]
        public int ShortOverdueDays { get; set; } = 7;

        /// <summary>
        /// Items in the system for this many days or more (and not resolved) are flagged as "Long Overdue".
        /// Previously hardcoded as 30. Shown on dashboards and Search.
        /// </summary>
        [Range(1, 3650)]
        [Display(Name = "Long Overdue Threshold (days)")]
        public int LongOverdueDays { get; set; } = 30;
    }
}
