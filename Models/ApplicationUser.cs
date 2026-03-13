using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    // Extending IdentityUser to add application-specific fields for AD integration and password change enforcement
    public class ApplicationUser : IdentityUser
    {
        [StringLength(200)]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Indicates whether this user was synced from Active Directory
        /// </summary>
        public bool IsAdUser { get; set; } = false;

        /// <summary>
        /// When true, local users must change password on next login. AD users are exempt.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Indicates whether the user account is active. Deactivated users cannot log in.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// SAM Account Name from Active Directory for AD-synced users
        /// </summary>
        [StringLength(256)]
        public string? SamAccountName { get; set; }
    }
}
