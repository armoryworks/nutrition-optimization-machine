export interface MeasurementCategoryModel {
    id: number;
    name: string;
    description?: string;
    baseUnitId?: number;
    baseUnitName?: string;
    baseUnitSymbol?: string;
    createdDate: Date;
    lastModifiedDate?: Date;
    authorId: number;
    createdById: number;
    userId: number;
}
