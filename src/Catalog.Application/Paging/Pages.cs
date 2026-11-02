namespace Catalog.Application.Paging;

internal static class Pages
{
    /// <summary>
    /// Builds a page from the rows that a store returned. The handler asked for one row more than the page size, so a row beyond the size means that a next page exists, and the cursor names the last row
    /// that the page keeps.
    /// </summary>
    public static Page<T> Build<T>(IReadOnlyList<T> rows, int size, string sort, Func<T, KeysetPosition> positionOf)
    {
        if (rows.Count <= size)
        {
            return new Page<T>(rows, null);
        }

        var items = rows.Take(size).ToList();

        return new Page<T>(items, PageCursor.Encode(sort, positionOf(items[^1])));
    }
}
