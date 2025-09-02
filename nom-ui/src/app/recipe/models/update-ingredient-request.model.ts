// File: nom-ui/src/app/recipe/models/update-ingredient-request.model.ts

export interface UpdateIngredientRequestModel {
    id: number;
    name: string;
    description?: string;
    nutrients: NutrientValueModel[];
}

export interface NutrientValueModel {
    nutrientId: number;
    amount: number;
    measurementId: number;
}
