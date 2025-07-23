import { LabelNutrient } from './label-nutrient';

/** Represents all data needed for the nutrition label component. */
export interface NutritionLabelData {
    servingsPerContainer: string;
    servingSizeHousehold: string;
    servingSizeGrams: number;
    calories: number;
    nutrients: LabelNutrient[];
  }