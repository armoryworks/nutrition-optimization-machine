export interface CreateConversionRequest {
    fromMeasurementId: number;
    toMeasurementId: number;
    conversionFactor: number;
    offset?: number;
    formula?: string;
    isDirectConversion: boolean;
}

