using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Single-row table storing the application's password policy settings.
    /// Persisted in the database so SuperAdmin changes take effect immediately.
    /// </summary>
    public class PasswordPolicySetting
    {
        public int Id { get; set; }

        [Range(6, 128)]
        [Display(Name = "Minimum Length")]
        public int MinimumLength { get; set; } = 8;

        [Display(Name = "Require Digit")]
        public bool RequireDigit { get; set; } = true;

        [Display(Name = "Require Lowercase")]
        public bool RequireLowercase { get; set; } = true;

        [Display(Name = "Require Uppercase")]
        public bool RequireUppercase { get; set; } = true;

        [Display(Name = "Require Special Character")]
        public bool RequireNonAlphanumeric { get; set; } = true;
    }
}
