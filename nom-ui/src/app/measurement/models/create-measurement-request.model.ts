export interface CreateMeasurementRequest {
    name: string;
    description?: string;
    symbol: string;
    categoryId: number;
    isBaseUnit: boolean;
    baseUnitConversionFactor?: number;
}

