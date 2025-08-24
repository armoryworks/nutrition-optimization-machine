import { MeasurementModel } from './measurement.model';

export interface NutrientMeasurementModel extends MeasurementModel {
    nutrientId: number;
    nutrientName: string;
    standardAmount?: number;
    isStandardUnit: boolean;
}

