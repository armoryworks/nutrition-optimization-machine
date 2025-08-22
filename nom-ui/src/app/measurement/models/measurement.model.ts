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
}

export interface MeasurementCategoryModel {
    id: number;
    name: string;
    description?: string;
    baseUnitId?: number;
    baseUnitName?: string;
    baseUnitSymbol?: string;
    createdDate: Date;
    lastModifiedDate?: Date;
}

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
}

export interface IngredientMeasurementModel extends MeasurementModel {
    ingredientId: number;
    ingredientName: string;
    typicalQuantity?: number;
    isPreferredUnit: boolean;
}

export interface NutrientMeasurementModel extends MeasurementModel {
    nutrientId: number;
    nutrientName: string;
    standardAmount?: number;
    isStandardUnit: boolean;
}

export interface CreateMeasurementRequest {
    name: string;
    description?: string;
    symbol: string;
    categoryId: number;
    isBaseUnit: boolean;
    baseUnitConversionFactor?: number;
}

export interface UpdateMeasurementRequest {
    id: number;
    name: string;
    description?: string;
    symbol: string;
    categoryId: number;
    isBaseUnit: boolean;
    baseUnitConversionFactor?: number;
}

export interface CreateConversionRequest {
    fromMeasurementId: number;
    toMeasurementId: number;
    conversionFactor: number;
    offset?: number;
    formula?: string;
    isDirectConversion: boolean;
}

export interface CreateCategoryRequest {
    name: string;
    description?: string;
    baseUnitId?: number;
}

export interface UpdateCategoryRequest {
    id: number;
    name: string;
    description?: string;
    baseUnitId?: number;
}

