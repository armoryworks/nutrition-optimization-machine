import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of } from 'rxjs';
import { map, tap, catchError } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface ReferenceItem {
    referenceId: number;
    referenceName: string;
    referenceDescription: string;
    groupId: number;
    groupName: string;
    groupDescription: string;
}

export interface ReferenceGroup {
    [groupId: number]: ReferenceItem[];
}

@Injectable({
    providedIn: 'root'
})
export class ReferenceDataService {
    private readonly baseUrl = `${environment.apiUrl}/api/Reference`;

    // Cache for reference data
    private referenceCache = new Map<number, ReferenceItem[]>();
    private bulkCache = new Map<string, ReferenceGroup>();

    constructor(private http: HttpClient) { }

    /**
     * Gets all references for a specific reference group
     */
    getReferencesByGroup(discriminatorId: number): Observable<ReferenceItem[]> {
        // Check cache first
        if (this.referenceCache.has(discriminatorId)) {
            return of(this.referenceCache.get(discriminatorId)!);
        }

        return this.http.get<ReferenceItem[]>(`${this.baseUrl}/${discriminatorId}/all`).pipe(
            tap(references => {
                // Cache the results
                this.referenceCache.set(discriminatorId, references);
            }),
            catchError(error => {
                console.error(`Error fetching references for group ${discriminatorId}:`, error);
                return of([]);
            })
        );
    }

    /**
     * Gets multiple reference groups in one call for performance
     */
    getReferencesBulk(discriminatorIds: number[]): Observable<ReferenceGroup> {
        // Create cache key
        const cacheKey = discriminatorIds.sort().join(',');

        // Check cache first
        if (this.bulkCache.has(cacheKey)) {
            return of(this.bulkCache.get(cacheKey)!);
        }

        return this.http.post<ReferenceGroup>(`${this.baseUrl}/bulk`, discriminatorIds).pipe(
            tap(references => {
                // Cache the results
                this.bulkCache.set(cacheKey, references);

                // Also update individual caches
                Object.entries(references).forEach(([groupId, items]) => {
                    this.referenceCache.set(parseInt(groupId), items);
                });
            }),
            catchError(error => {
                console.error('Error fetching references in bulk:', error);
                return of({});
            })
        );
    }

    /**
     * Clears the cache for a specific group
     */
    clearCache(discriminatorId?: number): void {
        if (discriminatorId) {
            this.referenceCache.delete(discriminatorId);
            // Clear bulk cache entries that contain this group
            this.bulkCache.forEach((value, key) => {
                if (key.includes(discriminatorId.toString())) {
                    this.bulkCache.delete(key);
                }
            });
        } else {
            // Clear all caches
            this.referenceCache.clear();
            this.bulkCache.clear();
        }
    }

    /**
     * Gets a specific reference item by ID from a group
     */
    getReferenceById(groupId: number, referenceId: number): Observable<ReferenceItem | null> {
        return this.getReferencesByGroup(groupId).pipe(
            map(references => references.find(ref => ref.referenceId === referenceId) || null)
        );
    }

    /**
     * Gets reference items by name pattern from a group
     */
    getReferencesByNamePattern(groupId: number, pattern: string): Observable<ReferenceItem[]> {
        return this.getReferencesByGroup(groupId).pipe(
            map(references =>
                references.filter(ref =>
                    ref.referenceName.toLowerCase().includes(pattern.toLowerCase())
                )
            )
        );
    }
}
