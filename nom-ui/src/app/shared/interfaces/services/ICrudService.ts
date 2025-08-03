// File: nom-ui/src/app/shared/interfaces/services/ICrudService.ts

import { Observable } from 'rxjs';
import { IBaseService } from './IBaseService';
import { IPagedResult } from './IPagedResult';
import { IValidationResult } from './IValidationResult';

/**
 * Base interface for CRUD services
 */
export interface ICrudService<T, TId = number> extends IBaseService {
    /**
     * Gets all items
     */
    getAll(): Observable<T[]>;

    /**
     * Gets an item by ID
     */
    getById(id: TId): Observable<T | null>;

    /**
     * Creates a new item
     */
    create(item: Partial<T>): Observable<T>;

    /**
     * Updates an existing item
     */
    update(id: TId, item: Partial<T>): Observable<T>;

    /**
     * Deletes an item
     */
    delete(id: TId): Observable<boolean>;

    /**
     * Checks if an item exists
     */
    exists(id: TId): Observable<boolean>;

    /**
     * Gets items with pagination
     */
    getPaged(page: number, pageSize: number): Observable<IPagedResult<T>>;

    /**
     * Searches for items
     */
    search(query: string): Observable<T[]>;

    /**
     * Validates an item
     */
    validate(item: Partial<T>): Observable<IValidationResult>;
} 