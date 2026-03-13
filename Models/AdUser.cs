using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Stores individual Active Directory usernames for user synchronization,
    /// with a mapped application role for automatic role assignment on sync.
    /// </summary>
    public class AdUser
    {
        public int Id { get; set; }

        [Required, StringLength(256)]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The application role that this AD user will be assigned.
        /// Must be one of: Admin, Supervisor, User
        /// </summary>
        [Required, StringLength(50)]
        [Display(Name = "Mapped Application Role")]
        public string MappedRole { get; set; } = "User";

        [Display(Name = "Date Added")]
        public DateTime DateAdded { get; set; }

        /// <summary>
        /// Whether this user is actively synced
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
