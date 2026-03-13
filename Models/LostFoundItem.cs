using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Primary tracking table for lost and found items. Uses auto-increment integer PK for simplicity,
    /// readability in URLs, and efficient indexing compared to GUID.
    /// </summary>
    public class LostFoundItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int TrackingId { get; set; }
        
        [StringLength(20)]
        [Display(Name = "Tracking ID")]
        public string? CustomTrackingId { get; set; }

        [Required(ErrorMessage = "Date Found is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Date Found")]
        [NotFutureDate(ErrorMessage = "Date Found cannot be in the future.")]
        public DateTime DateFound { get; set; }

        [Required(ErrorMessage = "Item is required.")]
        [Display(Name = "Item")]
        public int ItemId { get; set; }

        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }

        [StringLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Location Found is required.")]
        [StringLength(300)]
        [Display(Name = "Location Found")]
        public string LocationFound { get; set; } = string.Empty;

        [Display(Name = "Route #")]
        public int? RouteId { get; set; }

        [ForeignKey("RouteId")]
        public virtual Route? Route { get; set; }

        [Display(Name = "Vehicle #")]
        public int? VehicleId { get; set; }

        [ForeignKey("VehicleId")]
        public virtual Vehicle? Vehicle { get; set; }

        [StringLength(500)]
        [Display(Name = "Photo 1")]
        public string? PhotoPath { get; set; }

        [StringLength(500)]
        [Display(Name = "Photo 2")]
        public string? PhotoPath2 { get; set; }

        [StringLength(500)]
        [Display(Name = "Photo 3")]
        public string? PhotoPath3 { get; set; }

        [StringLength(500)]
        [Display(Name = "Photo 4")]
        public string? PhotoPath4 { get; set; }

        [Display(Name = "Storage Location")]
        public int? StorageLocationId { get; set; }

        [ForeignKey("StorageLocationId")]
        public virtual StorageLocation? StorageLocation { get; set; }

        [Required(ErrorMessage = "Status is required.")]
        [Display(Name = "Status")]
        public int StatusId { get; set; }

        [ForeignKey("StatusId")]
        public virtual Status? Status { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Status Date")]
        public DateTime? StatusDate { get; set; }

        // DaysSinceFound is never stored — calculated at read time
        [NotMapped]
        [Display(Name = "Days Since Found")]
        public int DaysSinceFound => (DateTime.Today - DateFound.Date).Days;

        [Display(Name = "Found By")]
        public int? FoundById { get; set; }

        [ForeignKey("FoundById")]
        public virtual FoundByName? FoundBy { get; set; }

        [StringLength(200)]
        [Display(Name = "Claimed By")]
        public string? ClaimedBy { get; set; }

        /// <summary>
        /// Username of the user who created the record — auto-populated from session, never user-editable.
        /// </summary>
        [StringLength(256)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Timestamp of record creation — auto-populated server-side UTC, never user-editable.
        /// </summary>
        [Display(Name = "Created Date/Time")]
        public DateTime CreatedDateTime { get; set; }

        /// <summary>
        /// Username of the user who last modified the record — auto-populated on edit, never user-editable.
        /// </summary>
        [StringLength(256)]
        [Display(Name = "Modified By")]
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Timestamp of last modification — auto-populated server-side UTC on edit, never user-editable.
        /// </summary>
        [Display(Name = "Modified Date/Time")]
        public DateTime? ModifiedDateTime { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        [StringLength(500)]
        [Display(Name = "Attachment")]
        public string? AttachmentPath { get; set; }
    }
}
