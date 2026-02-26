using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Stores Active Directory group names for user synchronization,
    /// with a mapped application role for automatic role assignment on sync.
    /// </summary>
    public class AdGroup
    {
        public int Id { get; set; }

        [Required, StringLength(256)]
        [Display(Name = "AD Group Name")]
        public string GroupName { get; set; } = string.Empty;

        /// <summary>
        /// The application role that members of this AD group will be assigned.
        /// Must be one of: Admin, User
        /// </summary>
        [Required, StringLength(50)]
        [Display(Name = "Mapped Application Role")]
        public string MappedRole { get; set; } = "User";

        [Display(Name = "Date Added")]
        public DateTime DateAdded { get; set; }

        /// <summary>
        /// Whether this group is actively synced
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
