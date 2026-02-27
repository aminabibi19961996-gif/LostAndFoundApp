namespace LostAndFoundApp.ViewModels
{
    /// <summary>
    /// Generic paginated list wrapper for MasterData pages
    /// </summary>
    public class PaginatedList<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPreviousPage => PageIndex > 1;
        public bool HasNextPage => PageIndex < TotalPages;
        public int StartRecord => TotalCount == 0 ? 0 : ((PageIndex - 1) * PageSize) + 1;
        public int EndRecord => Math.Min(PageIndex * PageSize, TotalCount);
    }

    /// <summary>
    /// Shared pagination parameters for all list actions
    /// </summary>
    public class PaginationParams
    {
        public const int DefaultPageSize = 10;
        public const int MaxPageSize = 50;
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = DefaultPageSize;
    }
}