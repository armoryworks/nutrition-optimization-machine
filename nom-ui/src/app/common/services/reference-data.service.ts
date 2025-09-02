import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, combineLatest } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { ReferenceItemModel } from '../models/reference-item.model';

@Injectable({
    providedIn: 'root'
})
export class ReferenceDataService {
    private http = inject(HttpClient);

    // Cache for reference data to avoid repeated API calls
    private cache = new Map<number, ReferenceItemModel[]>();

    /**
     * Gets reference data by group ID with caching
     */
    getReferencesByGroup(groupId: number): Observable<ReferenceItemModel[]> {
        // Check cache first
        if (this.cache.has(groupId)) {
            return of(this.cache.get(groupId)!);
        }

        return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/reference/${groupId}/all`).pipe(
            map(data => {
                // Cache the result
                this.cache.set(groupId, data);
                return data;
            }),
            catchError(error => {
                console.error(`Error loading reference data for group ${groupId}:`, error);
                return of([]);
            })
        );
    }

    /**
     * Gets measurement types
     */
    getMeasurementTypes(): Observable<ReferenceItemModel[]> {
        return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/measurement/all`).pipe(
            catchError(error => {
                console.error('Error loading measurement types:', error);
                return of([]);
            })
        );
    }

    /**
     * Gets measurements by category
     */
    getMeasurementsByCategory(categoryId: number): Observable<ReferenceItemModel[]> {
        return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/measurement/by-category/${categoryId}`).pipe(
            catchError(error => {
                console.error(`Error loading measurements for category ${categoryId}:`, error);
                return of([]);
            })
        );
    }

    /**
     * Gets nutrient types
     */
    getNutrientTypes(): Observable<ReferenceItemModel[]> {
        // Using the dedicated nutrient endpoint instead of reference system
        return this.http.get<any[]>(`${environment.apiUrl}/nutrient/all`).pipe(
            map(nutrients => nutrients.map(nutrient => ({
                id: nutrient.id,
                name: nutrient.name,
                description: nutrient.description,
                referenceId: nutrient.id // Map to referenceId for compatibility
            }))),
            catchError(error => {
                console.error('Error loading nutrient types:', error);
                return of([]);
            })
        );
    }

    /**
     * Gets restriction types
     */
    getRestrictionTypes(): Observable<ReferenceItemModel[]> {
        // Using the restriction type reference group (2000 from the enum)
        return this.getReferencesByGroup(2000);
    }

    /**
     * Gets meal types
     */
    getMealTypes(): Observable<ReferenceItemModel[]> {
        // Using the meal type reference group (1 from the enum)
        return this.getReferencesByGroup(1);
    }

    /**
     * Gets days of week
     */
    getDaysOfWeek(): Observable<ReferenceItemModel[]> {
        // This might need a specific reference group or could be hardcoded as it's standard
        return of([
            { id: 1, name: 'Monday' },
            { id: 2, name: 'Tuesday' },
            { id: 3, name: 'Wednesday' },
            { id: 4, name: 'Thursday' },
            { id: 5, name: 'Friday' },
            { id: 6, name: 'Saturday' },
            { id: 7, name: 'Sunday' }
        ]);
    }

    /**
     * Gets difficulty levels
     */
    getDifficultyLevels(): Observable<ReferenceItemModel[]> {
        return of([
            { id: 1, name: 'Easy' },
            { id: 2, name: 'Medium' },
            { id: 3, name: 'Hard' }
        ]);
    }

    /**
     * Clears the cache (useful for testing or when data might have changed)
     */
    clearCache(): void {
        this.cache.clear();
    }

    /**
     * Gets a specific reference item by ID from a group
     */
    getReferenceById(groupId: number, referenceId: number): Observable<ReferenceItemModel | null> {
        return this.getReferencesByGroup(groupId).pipe(
            map(references => references.find(ref => ref.id === referenceId) || null)
        );
    }

    /**
     * Gets multiple reference groups in one call for performance
     */
    getReferencesBulk(groupIds: number[]): Observable<{ [groupId: number]: ReferenceItemModel[] }> {
        const requests = groupIds.map(groupId =>
            this.getReferencesByGroup(groupId).pipe(
                map(references => ({ groupId, references }))
            )
        );

        return combineLatest(requests).pipe(
            map(results => {
                const bulkResult: { [groupId: number]: ReferenceItemModel[] } = {};
                results.forEach(({ groupId, references }) => {
                    bulkResult[groupId] = references;
                });
                return bulkResult;
            })
        );
    }

    /**
     * Clears cache for a specific group
     */
    clearCacheForGroup(groupId: number): void {
        this.cache.delete(groupId);
    }
}