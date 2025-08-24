export interface MeasurementConversionModel {
    id: number;
    fromMeasurementId: number;
    fromMeasurementName: string;
    fromMeasurementSymbol: string;
    toMeasurementId: number;
    toMeasurementName: string;
    toMeasurementSymbol: string;
    conversionFactor: number;
    offset?: number;
    formula?: string;
    isDirectConversion: boolean;
    createdDate: Date;
    lastModifiedDate?: Date;
    authorId: number;
    createdById: number;
    userId: number;
}
