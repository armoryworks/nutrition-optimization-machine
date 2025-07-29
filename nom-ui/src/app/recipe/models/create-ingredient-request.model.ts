// File: nom-ui/src/app/recipe/models/create-ingredient-request.model.ts

export interface CreateIngredientRequestModel {
    name: string;
    description?: string;
    nutrients: NutrientValueModel[];
}

export interface NutrientValueModel {
    nutrientId: string;
    amount: number;
    measurementTypeId: string;
} 