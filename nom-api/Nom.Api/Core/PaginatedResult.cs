namespace Nom.Api.Core
{
    /// <summary>
    /// Result of a paginated query
    /// </summary>
    /// <typeparam name="T">The type of items</typeparam>
    public class PaginatedResult<T>
    {
        /// <summary>
        /// Gets the items in the current page
        /// </summary>
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();

        /// <summary>
        /// Gets the total number of items
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets the current page number
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Gets the page size
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets the total number of pages
        /// </summary>
        public int TotalPages { get { return (int)Math.Ceiling((double)TotalCount / PageSize); } }

        /// <summary>
        /// Gets whether there is a previous page
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// Gets whether there is a next page
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;
    }
}