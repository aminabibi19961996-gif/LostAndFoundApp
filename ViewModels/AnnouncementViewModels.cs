using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.ViewModels
{
    public class CreateAnnouncementViewModel
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200, ErrorMessage = "Title must not exceed 200 characters.")]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Message is required.")]
        [StringLength(4000, ErrorMessage = "Message must not exceed 4000 characters.")]
        [Display(Name = "Message")]
        public string Message { get; set; } = string.Empty;

        [Required(ErrorMessage = "Target audience is required.")]
        [Display(Name = "Send To")]
        public string TargetRole { get; set; } = "All";

        [Display(Name = "Expires On")]
        [DataType(DataType.Date)]
        public DateTime? ExpiresAt { get; set; }
    }

    public class AnnouncementListViewModel
    {
        public List<AnnouncementListItem> Announcements { get; set; } = new();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalPages { get; set; }
    }

    public class AnnouncementListItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TargetRole { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; }
        public int ReadCount { get; set; }
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    }

    public class UserMessageViewModel
    {
        public List<UserMessageItem> Messages { get; set; } = new();
        public int UnreadCount { get; set; }
    }

    public class UserMessageItem
    {
        public int Id { get; set; }
        public int AnnouncementId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsDismissed { get; set; }
        public DateTime? DismissedAt { get; set; }
    }
}
