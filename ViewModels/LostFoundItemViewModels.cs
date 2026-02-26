using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.ViewModels
{
    public class LostFoundItemCreateViewModel
    {
        [Required(ErrorMessage = "Date found is required.")]
        [Display(Name = "Date Found")]
        [DataType(DataType.Date)]
        [NotFutureDate(ErrorMessage = "Date Found cannot be in the future.")]
        public DateTime DateFound { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Item type is required.")]
        [Display(Name = "Item Type")]
        public int ItemId { get; set; }

        [StringLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Location found is required.")]
        [StringLength(300)]
        [Display(Name = "Location Found")]
        public string LocationFound { get; set; } = string.Empty;

        [Display(Name = "Route #")]
        public int? RouteId { get; set; }

        [Display(Name = "Vehicle #")]
        public int? VehicleId { get; set; }

        [Display(Name = "Storage Location")]
        public int? StorageLocationId { get; set; }

        [Required(ErrorMessage = "Status is required.")]
        [Display(Name = "Status")]
        public int StatusId { get; set; }

        [Display(Name = "Status Date")]
        [DataType(DataType.Date)]
        public DateTime? StatusDate { get; set; } = DateTime.Today;

        [Display(Name = "Found By")]
        public int? FoundById { get; set; }

        [StringLength(200)]
        [Display(Name = "Claimed By")]
        public string? ClaimedBy { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        [Display(Name = "Photo")]
        public IFormFile? PhotoFile { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile? AttachmentFile { get; set; }

        // Dropdown data
        public SelectList? Items { get; set; }
        public SelectList? Routes { get; set; }
        public SelectList? Vehicles { get; set; }
        public SelectList? StorageLocations { get; set; }
        public SelectList? Statuses { get; set; }
        public SelectList? FoundByNames { get; set; }
    }

    public class LostFoundItemEditViewModel
    {
        public int TrackingId { get; set; }

        [Required(ErrorMessage = "Date found is required.")]
        [Display(Name = "Date Found")]
        [DataType(DataType.Date)]
        [NotFutureDate(ErrorMessage = "Date Found cannot be in the future.")]
        public DateTime DateFound { get; set; }

        [Required(ErrorMessage = "Item type is required.")]
        [Display(Name = "Item Type")]
        public int ItemId { get; set; }

        [StringLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Location found is required.")]
        [StringLength(300)]
        [Display(Name = "Location Found")]
        public string LocationFound { get; set; } = string.Empty;

        [Display(Name = "Route #")]
        public int? RouteId { get; set; }

        [Display(Name = "Vehicle #")]
        public int? VehicleId { get; set; }

        [Display(Name = "Storage Location")]
        public int? StorageLocationId { get; set; }

        [Required(ErrorMessage = "Status is required.")]
        [Display(Name = "Status")]
        public int StatusId { get; set; }

        [Display(Name = "Status Date")]
        [DataType(DataType.Date)]
        public DateTime? StatusDate { get; set; }

        [Display(Name = "Found By")]
        public int? FoundById { get; set; }

        [StringLength(200)]
        [Display(Name = "Claimed By")]
        public string? ClaimedBy { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        [Display(Name = "Photo")]
        public IFormFile? PhotoFile { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile? AttachmentFile { get; set; }

        // Read-only display
        public string? ExistingPhotoPath { get; set; }
        public string? ExistingAttachmentPath { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedDateTime { get; set; }

        // Dropdown data
        public SelectList? Items { get; set; }
        public SelectList? Routes { get; set; }
        public SelectList? Vehicles { get; set; }
        public SelectList? StorageLocations { get; set; }
        public SelectList? Statuses { get; set; }
        public SelectList? FoundByNames { get; set; }
    }

    public class LostFoundItemDetailViewModel
    {
        public int TrackingId { get; set; }
        public DateTime DateFound { get; set; }
        public string? ItemName { get; set; }
        public string? Description { get; set; }
        public string LocationFound { get; set; } = string.Empty;
        public string? RouteName { get; set; }
        public string? VehicleName { get; set; }
        public string? PhotoPath { get; set; }
        public string? StorageLocationName { get; set; }
        public string? StatusName { get; set; }
        public DateTime? StatusDate { get; set; }
        public int DaysSinceFound { get; set; }
        public string? FoundByName { get; set; }
        public string? ClaimedBy { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedDateTime { get; set; }
        public string? Notes { get; set; }
        public string? AttachmentPath { get; set; }
    }

    public class SearchViewModel
    {
        // Filters
        public int? TrackingId { get; set; }
        public DateTime? DateFoundFrom { get; set; }
        public DateTime? DateFoundTo { get; set; }
        public int? ItemId { get; set; }
        public int? StatusId { get; set; }
        public int? RouteId { get; set; }
        public int? VehicleId { get; set; }
        public int? StorageLocationId { get; set; }
        public int? FoundById { get; set; }

        // Sort
        public string SortField { get; set; } = "TrackingId";
        public string SortOrder { get; set; } = "desc";

        // Pagination
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }

        // Filter summary for print
        public string FilterSummary { get; set; } = string.Empty;

        // Results
        public List<SearchResultItem> Results { get; set; } = new();

        // Dropdown data
        public SelectList? Items { get; set; }
        public SelectList? Statuses { get; set; }
        public SelectList? Routes { get; set; }
        public SelectList? Vehicles { get; set; }
        public SelectList? StorageLocations { get; set; }
        public SelectList? FoundByNames { get; set; }
    }

    public class SearchResultItem
    {
        public int TrackingId { get; set; }
        public DateTime DateFound { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string LocationFound { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string VehicleName { get; set; } = string.Empty;
        public string StorageLocationName { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public int DaysSinceFound { get; set; }
        public string FoundByName { get; set; } = string.Empty;
        public string? ClaimedBy { get; set; }
    }
}
