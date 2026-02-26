using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Tracks per-user read/dismiss state for each announcement.
    /// PopupShownCount controls how many times the popup has been displayed (max 3).
    /// </summary>
    public class AnnouncementRead
    {
        public int Id { get; set; }

        [Required]
        public int AnnouncementId { get; set; }

        [ForeignKey("AnnouncementId")]
        public virtual Announcement? Announcement { get; set; }

        [Required, StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Number of times the popup has been shown to this user (stops at 3).
        /// </summary>
        public int PopupShownCount { get; set; } = 0;

        public DateTime FirstReadAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the user explicitly dismissed the announcement. Null if not yet dismissed.
        /// </summary>
        public DateTime? DismissedAt { get; set; }
    }
}
