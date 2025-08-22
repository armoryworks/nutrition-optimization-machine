import { Injectable } from '@angular/core';
import { Observable, combineLatest } from 'rxjs';
import { map } from 'rxjs/operators';
import { ReferenceDataService, ReferenceItem } from '../../common/services/reference-data.service';
import { REFERENCE_IDS } from '../../common/constants/reference-ids';

@Injectable({
    providedIn: 'root'
})
export class MealPlanReferenceService {
    constructor(private referenceDataService: ReferenceDataService) { }

    /**
     * Gets all meal types
     */
    getMealTypes(): Observable<ReferenceItem[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.MEAL_TYPE);
    }

    /**
     * Gets all days of week
     */
    getDaysOfWeek(): Observable<ReferenceItem[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.DAY_OF_WEEK_TYPE);
    }

    /**
     * Gets meal types and days of week in one call
     */
    getMealPlanReferences(): Observable<{
        mealTypes: ReferenceItem[];
        daysOfWeek: ReferenceItem[];
    }> {
        return combineLatest([
            this.getMealTypes(),
            this.getDaysOfWeek()
        ]).pipe(
            map(([mealTypes, daysOfWeek]) => ({
                mealTypes,
                daysOfWeek
            }))
        );
    }

    /**
     * Gets a meal type by ID
     */
    getMealTypeById(mealTypeId: number): Observable<ReferenceItem | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.MEAL_TYPE, mealTypeId);
    }

    /**
     * Gets a day of week by ID
     */
    getDayOfWeekById(dayId: number): Observable<ReferenceItem | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.DAY_OF_WEEK_TYPE, dayId);
    }

    /**
     * Gets meal plan references with bulk loading for performance
     */
    getMealPlanReferencesBulk(): Observable<{
        mealTypes: ReferenceItem[];
        daysOfWeek: ReferenceItem[];
    }> {
        return this.referenceDataService.getReferencesBulk([
            REFERENCE_IDS.MEAL_TYPE,
            REFERENCE_IDS.DAY_OF_WEEK_TYPE
        ]).pipe(
            map(references => ({
                mealTypes: references[REFERENCE_IDS.MEAL_TYPE] || [],
                daysOfWeek: references[REFERENCE_IDS.DAY_OF_WEEK_TYPE] || []
            }))
        );
    }

    /**
     * Clears meal plan reference cache
     */
    clearCache(): void {
        this.referenceDataService.clearCache(REFERENCE_IDS.MEAL_TYPE);
        this.referenceDataService.clearCache(REFERENCE_IDS.DAY_OF_WEEK_TYPE);
    }
}
