import { Injectable } from '@angular/core';
import { Observable, combineLatest } from 'rxjs';
import { map } from 'rxjs/operators';
import { ReferenceDataService, ReferenceItem } from '../../common/services/reference-data.service';
import { REFERENCE_IDS } from '../../common/constants/reference-ids';

@Injectable({
    providedIn: 'root'
})
export class RecipeReferenceService {
    constructor(private referenceDataService: ReferenceDataService) { }

    /**
     * Gets all recipe difficulty types
     */
    getRecipeDifficulties(): Observable<ReferenceItem[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE);
    }

    /**
     * Gets all cuisine types
     */
    getCuisineTypes(): Observable<ReferenceItem[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.CUISINE_TYPE);
    }

    /**
     * Gets all meal types
     */
    getMealTypes(): Observable<ReferenceItem[]> {
        return this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.MEAL_TYPE);
    }

    /**
     * Gets recipe references in one call
     */
    getRecipeReferences(): Observable<{
        difficulties: ReferenceItem[];
        cuisines: ReferenceItem[];
        mealTypes: ReferenceItem[];
    }> {
        return combineLatest([
            this.getRecipeDifficulties(),
            this.getCuisineTypes(),
            this.getMealTypes()
        ]).pipe(
            map(([difficulties, cuisines, mealTypes]) => ({
                difficulties,
                cuisines,
                mealTypes
            }))
        );
    }

    /**
     * Gets a recipe difficulty by ID
     */
    getRecipeDifficultyById(difficultyId: number): Observable<ReferenceItem | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE, difficultyId);
    }

    /**
     * Gets a cuisine type by ID
     */
    getCuisineTypeById(cuisineId: number): Observable<ReferenceItem | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.CUISINE_TYPE, cuisineId);
    }

    /**
     * Gets a meal type by ID
     */
    getMealTypeById(mealTypeId: number): Observable<ReferenceItem | null> {
        return this.referenceDataService.getReferenceById(REFERENCE_IDS.MEAL_TYPE, mealTypeId);
    }

    /**
     * Gets recipe references with bulk loading for performance
     */
    getRecipeReferencesBulk(): Observable<{
        difficulties: ReferenceItem[];
        cuisines: ReferenceItem[];
        mealTypes: ReferenceItem[];
        dietaryOptions: ReferenceItem[];
        allergens: ReferenceItem[];
    }> {
        return this.referenceDataService.getReferencesBulk([
            REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE,
            REFERENCE_IDS.CUISINE_TYPE,
            REFERENCE_IDS.MEAL_TYPE,
            REFERENCE_IDS.RECIPE_DIETARY_OPTION_TYPE,
            REFERENCE_IDS.ALLERGY_TYPE
        ]).pipe(
            map(references => ({
                difficulties: references[REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE] || [],
                cuisines: references[REFERENCE_IDS.CUISINE_TYPE] || [],
                mealTypes: references[REFERENCE_IDS.MEAL_TYPE] || [],
                dietaryOptions: references[REFERENCE_IDS.RECIPE_DIETARY_OPTION_TYPE] || [],
                allergens: references[REFERENCE_IDS.ALLERGY_TYPE] || []
            }))
        );
    }

    /**
     * Clears recipe reference cache
     */
    clearCache(): void {
        this.referenceDataService.clearCache(REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE);
        this.referenceDataService.clearCache(REFERENCE_IDS.CUISINE_TYPE);
        this.referenceDataService.clearCache(REFERENCE_IDS.MEAL_TYPE);
    }
}
