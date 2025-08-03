// File: nom-ui/src/app/shared/interfaces/services/IPagedResult.ts

/**
 * Paged result interface
 */
export interface IPagedResult<T> {
    /**
     * The items in the current page
     */
    items: T[];

    /**
     * The total number of items
     */
    totalCount: number;

    /**
     * The current page number
     */
    page: number;

    /**
     * The page size
     */
    pageSize: number;

    /**
     * The total number of pages
     */
    totalPages: number;

    /**
     * Whether there is a next page
     */
    hasNextPage: boolean;

    /**
     * Whether there is a previous page
     */
    hasPreviousPage: boolean;
} 