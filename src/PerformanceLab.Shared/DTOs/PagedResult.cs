namespace PerformanceLab.Shared.DTOs;

/// <summary>
/// Generic wrapper for paginated API responses.
/// Includes the requested items along with pagination metadata.
/// </summary>
/// <typeparam name="T">The type of items in the paginated result</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The items for the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// The total number of items available (across all pages).
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// The offset (zero-based index) of the first item in this page.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// The maximum number of items requested for this page.
    /// </summary>
    public int Limit { get; init; }
}
