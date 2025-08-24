import { MeasurementModel } from './measurement.model';

export interface IngredientMeasurementModel extends MeasurementModel {
    ingredientId: number;
    ingredientName: string;
    typicalQuantity?: number;
    isPreferredUnit: boolean;
}

