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

    // Cache for reference data with TTL (Time To Live)
    private referenceCache = new Map<number, { data: ReferenceItem[], timestamp: number }>();
    private bulkCache = new Map<string, { data: ReferenceGroup, timestamp: number }>();

    // Cache TTL in milliseconds (5 minutes)
    private readonly CACHE_TTL = 5 * 60 * 1000;

    constructor(private http: HttpClient) { }

    /**
     * Gets all references for a specific reference group
     */
    getReferencesByGroup(discriminatorId: number): Observable<ReferenceItem[]> {
        // Check cache first with TTL validation
        const cached = this.referenceCache.get(discriminatorId);
        if (cached && this.isCacheValid(cached.timestamp)) {
            return of(cached.data);
        }

        return this.http.get<ReferenceItem[]>(`${this.baseUrl}/${discriminatorId}/all`).pipe(
            tap(references => {
                // Cache the results with timestamp
                this.referenceCache.set(discriminatorId, {
                    data: references,
                    timestamp: Date.now()
                });
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

        // Check cache first with TTL validation
        const cached = this.bulkCache.get(cacheKey);
        if (cached && this.isCacheValid(cached.timestamp)) {
            return of(cached.data);
        }

        return this.http.post<ReferenceGroup>(`${this.baseUrl}/bulk`, discriminatorIds).pipe(
            tap(references => {
                // Cache the results with timestamp
                this.bulkCache.set(cacheKey, {
                    data: references,
                    timestamp: Date.now()
                });

                // Also update individual caches with timestamps
                Object.entries(references).forEach(([groupId, items]) => {
                    this.referenceCache.set(parseInt(groupId), {
                        data: items,
                        timestamp: Date.now()
                    });
                });
            }),
            catchError(error => {
                console.error('Error fetching references in bulk:', error);
                return of({});
            })
        );
    }

    /**
     * Checks if cache entry is still valid based on TTL
     */
    private isCacheValid(timestamp: number): boolean {
        return Date.now() - timestamp < this.CACHE_TTL;
    }

    /**
     * Clears expired cache entries
     */
    private clearExpiredCache(): void {
        const now = Date.now();

        // Clear expired individual cache entries
        this.referenceCache.forEach((value, key) => {
            if (!this.isCacheValid(value.timestamp)) {
                this.referenceCache.delete(key);
            }
        });

        // Clear expired bulk cache entries
        this.bulkCache.forEach((value, key) => {
            if (!this.isCacheValid(value.timestamp)) {
                this.bulkCache.delete(key);
            }
        });
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

    /**
     * Gets cache statistics for monitoring
     */
    getCacheStats(): { individualCacheSize: number, bulkCacheSize: number, totalEntries: number } {
        this.clearExpiredCache(); // Clean up expired entries first

        return {
            individualCacheSize: this.referenceCache.size,
            bulkCacheSize: this.bulkCache.size,
            totalEntries: this.referenceCache.size + this.bulkCache.size
        };
    }

    /**
     * Preloads commonly used reference groups for better performance
     */
    preloadCommonReferences(): void {
        const commonGroups = [6000, 6001, 6002, 6003, 6004]; // Shopping priorities, categories, etc.

        commonGroups.forEach(groupId => {
            if (!this.referenceCache.has(groupId)) {
                this.getReferencesByGroup(groupId).subscribe();
            }
        });
    }
}
