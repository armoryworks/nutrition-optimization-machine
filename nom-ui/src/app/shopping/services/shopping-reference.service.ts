import { Injectable } from '@angular/core';
import { Observable, combineLatest } from 'rxjs';
import { map } from 'rxjs/operators';
import { ReferenceDataService } from '../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../common/models/reference-item.model';
import { REFERENCE_IDS } from '../../common/constants/reference-ids';

@Injectable({
    providedIn: 'root'
})
export class ShoppingReferenceService {
    constructor(private referenceDataService: ReferenceDataService) { }

    /**
     * Gets all shopping priority types
     */
    getShoppingPriorities(): Observable<ReferenceItemModel[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.SHOPPING_PRIORITY_TYPE);
    }

    /**
     * Gets all shopping category types
     */
    getShoppingCategories(): Observable<ReferenceItemModel[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.SHOPPING_CATEGORY_TYPE);
    }

    /**
     * Gets shopping priorities and categories in one call
     */
    getShoppingReferences(): Observable<{
        priorities: ReferenceItemModel[];
        categories: ReferenceItemModel[];
    }> {
        return combineLatest([
            this.getShoppingPriorities(),
            this.getShoppingCategories()
        ]).pipe(
            map(([priorities, categories]) => ({
                priorities,
                categories
            }))
        );
    }

    /**
     * Gets a shopping priority by ID
     */
    getShoppingPriorityById(priorityId: number): Observable<ReferenceItemModel | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.SHOPPING_PRIORITY_TYPE, priorityId);
    }

    /**
     * Gets a shopping category by ID
     */
    getShoppingCategoryById(categoryId: number): Observable<ReferenceItemModel | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.SHOPPING_CATEGORY_TYPE, categoryId);
    }

    /**
     * Gets shopping priorities and categories with bulk loading for performance
     */
    getShoppingReferencesBulk(): Observable<{
        priorities: ReferenceItemModel[];
        categories: ReferenceItemModel[];
    }> {
        return this.referenceDataService.getReferencesBulk([
            REFERENCE_IDS.SHOPPING_PRIORITY_TYPE,
            REFERENCE_IDS.SHOPPING_CATEGORY_TYPE
        ]).pipe(
            map(references => ({
                priorities: references[REFERENCE_IDS.SHOPPING_PRIORITY_TYPE] || [],
                categories: references[REFERENCE_IDS.SHOPPING_CATEGORY_TYPE] || []
            }))
        );
    }

    /**
     * Clears shopping reference cache
     */
    clearCache(): void {
        this.referenceDataService.clearCacheForGroup(REFERENCE_IDS.SHOPPING_PRIORITY_TYPE);
        this.referenceDataService.clearCacheForGroup(REFERENCE_IDS.SHOPPING_CATEGORY_TYPE);
    }
}
