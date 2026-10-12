namespace TaskFlowAPI.Helpers;

public class QueryParameters
{
    private const int MaxPageSize = 100; // Hard ceiling to block performance text memory bombs
    public int PageNumber { get; set; } = 1;

    private int _pageSize = 10;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
    }

    // Optional Filtering Params
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }

    // Optional Sorting Params
    public string SortBy { get; set; } = "Id"; // Default column target
    public bool IsDescending { get; set; } = false;
}
