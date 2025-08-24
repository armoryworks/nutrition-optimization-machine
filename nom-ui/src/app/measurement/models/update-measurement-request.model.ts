export interface UpdateMeasurementRequest {
    id: number;
    name: string;
    description?: string;
    symbol: string;
    categoryId: number;
    isBaseUnit: boolean;
    baseUnitConversionFactor?: number;
}

