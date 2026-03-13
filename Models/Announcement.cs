using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Stores announcements created by SuperAdmin for Admin and/or User roles.
    /// Announcements appear as popups on login and persist in the user's message inbox.
    /// </summary>
    public class Announcement
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(4000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Target audience: "Admin", "User", or "All" (both Admin and User)
        /// </summary>
        [Required, StringLength(50)]
        public string TargetRole { get; set; } = "All";

        [Required, StringLength(256)]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional expiry date. Null means the announcement never expires.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
