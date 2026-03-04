using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, ErrorMessage = "Password must be between {2} and {1} characters.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class ProfileViewModel
    {
        public string UserName { get; set; } = string.Empty;

        [Display(Name = "Display Name")]
        [StringLength(200, ErrorMessage = "Display name cannot exceed 200 characters.")]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public string Email { get; set; } = string.Empty;

        public bool IsAdUser { get; set; }
    }

    public class ForgotUsernameViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;
    }

    public class PasswordPolicyViewModel
    {
        public int MinimumLength { get; set; } = 8;
        public bool RequireDigit { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireNonAlphanumeric { get; set; } = true;
    }

    public class OverdueSettingsViewModel
    {
        [Range(1, 365, ErrorMessage = "Short overdue threshold must be between 1 and 365 days.")]
        [Display(Name = "Short Overdue Threshold (days)")]
        public int ShortOverdueDays { get; set; } = 7;

        [Range(1, 3650, ErrorMessage = "Long overdue threshold must be between 1 and 3650 days.")]
        [Display(Name = "Long Overdue Threshold (days)")]
        public int LongOverdueDays { get; set; } = 30;
    }
}
