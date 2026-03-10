export interface RecipeIngredientModel {
  ingredientId: number;
  name: string;
  quantity: number;
  measurementId: number;
  measurement?: string;
  notes?: string;
}
