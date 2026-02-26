using LostAndFoundApp.Models;

namespace LostAndFoundApp.ViewModels
{
    public class LogListViewModel
    {
        public List<ActivityLog> Logs { get; set; } = new();
        public bool CanClearLogs { get; set; }
        public bool CanViewAllLogs { get; set; }
        public int TotalCount { get; set; }

        // Filters
        public string? CategoryFilter { get; set; }
        public string? SearchTerm { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }

        // Pagination
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalPages { get; set; }
    }
}
