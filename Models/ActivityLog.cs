using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Stores all application activity logs for audit trail.
    /// Super Admin can view and clear logs; other roles can only view.
    /// </summary>
    public class ActivityLog
    {
        public int Id { get; set; }

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Short action name, e.g. "Login", "AD Sync", "Create Item", "Edit Role"
        /// </summary>
        [Required, StringLength(100)]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what happened
        /// </summary>
        [Required, StringLength(2000)]
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Username of the person who performed the action
        /// </summary>
        [Required, StringLength(256)]
        public string PerformedBy { get; set; } = string.Empty;

        /// <summary>
        /// Category for filtering: Auth, ADSync, UserManagement, MasterData, Items, System
        /// </summary>
        [Required, StringLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Client IP address for security auditing
        /// </summary>
        [StringLength(50)]
        public string? IpAddress { get; set; }

        /// <summary>
        /// Success or Failure
        /// </summary>
        [StringLength(20)]
        public string Status { get; set; } = "Success";
    }
}
