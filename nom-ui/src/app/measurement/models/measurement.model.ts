export interface MeasurementModel {
    id: number;
    name: string;
    description?: string;
    symbol: string;
    categoryId: number;
    categoryName: string;
    isBaseUnit: boolean;
    baseUnitConversionFactor?: number;
    createdDate: Date;
    lastModifiedDate?: Date;
    authorId: number;
    createdById: number;
    userId: number;
}

