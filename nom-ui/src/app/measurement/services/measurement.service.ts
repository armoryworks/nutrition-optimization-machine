import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

import {
    MeasurementModel,
    MeasurementCategoryModel,
    MeasurementConversionModel,
    IngredientMeasurementModel,
    NutrientMeasurementModel,
    CreateMeasurementRequest,
    UpdateMeasurementRequest,
    CreateConversionRequest,
    CreateCategoryRequest,
    UpdateCategoryRequest
} from '../models/measurement.model';

@Injectable({
    providedIn: 'root'
})
export class MeasurementService {
    private readonly apiUrl = `${environment.apiUrl}/api/measurements`;

    constructor(private http: HttpClient) { }

    // Measurement CRUD operations
    getMeasurements(): Observable<MeasurementModel[]> {
        return this.http.get<MeasurementModel[]>(this.apiUrl);
    }

    getMeasurementById(id: number): Observable<MeasurementModel> {
        return this.http.get<MeasurementModel>(`${this.apiUrl}/${id}`);
    }

    getMeasurementsByCategory(categoryId: number): Observable<MeasurementModel[]> {
        return this.http.get<MeasurementModel[]>(`${this.apiUrl}/category/${categoryId}`);
    }

    createMeasurement(request: CreateMeasurementRequest): Observable<MeasurementModel> {
        return this.http.post<MeasurementModel>(this.apiUrl, request);
    }

    updateMeasurement(request: UpdateMeasurementRequest): Observable<MeasurementModel> {
        return this.http.put<MeasurementModel>(`${this.apiUrl}/${request.id}`, request);
    }

    deleteMeasurement(id: number): Observable<boolean> {
        return this.http.delete<boolean>(`${this.apiUrl}/${id}`);
    }

    // Conversion operations
    convertMeasurement(fromId: number, toId: number, value: number): Observable<number> {
        return this.http.post<number>(`${this.apiUrl}/convert`, { fromId, toId, value });
    }

    getConversionPaths(fromId: number, toId: number): Observable<MeasurementConversionModel[]> {
        return this.http.get<MeasurementConversionModel[]>(`${this.apiUrl}/conversions/${fromId}/${toId}`);
    }

    createConversion(request: CreateConversionRequest): Observable<MeasurementConversionModel> {
        return this.http.post<MeasurementConversionModel>(`${this.apiUrl}/conversions`, request);
    }

    // Ingredient-specific measurements
    getIngredientMeasurements(ingredientId: number): Observable<IngredientMeasurementModel[]> {
        return this.http.get<IngredientMeasurementModel[]>(`${this.apiUrl}/ingredient/${ingredientId}`);
    }

    // Nutrient-specific measurements
    getNutrientMeasurements(nutrientId: number): Observable<NutrientMeasurementModel[]> {
        return this.http.get<NutrientMeasurementModel[]>(`${this.apiUrl}/nutrient/${nutrientId}`);
    }
}

