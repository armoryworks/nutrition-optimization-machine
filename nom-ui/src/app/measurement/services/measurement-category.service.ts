import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

import {
    MeasurementCategoryModel,
    MeasurementModel,
    CreateCategoryRequest,
    UpdateCategoryRequest
} from '../models/measurement.model';

@Injectable({
    providedIn: 'root'
})
export class MeasurementCategoryService {
    private readonly apiUrl = `${environment.apiUrl}/api/measurement-categories`;

    constructor(private http: HttpClient) { }

    // Category CRUD operations
    getCategories(): Observable<MeasurementCategoryModel[]> {
        return this.http.get<MeasurementCategoryModel[]>(this.apiUrl);
    }

    getCategoryById(id: number): Observable<MeasurementCategoryModel> {
        return this.http.get<MeasurementCategoryModel>(`${this.apiUrl}/${id}`);
    }

    createCategory(request: CreateCategoryRequest): Observable<MeasurementCategoryModel> {
        return this.http.post<MeasurementCategoryModel>(this.apiUrl, request);
    }

    updateCategory(request: UpdateCategoryRequest): Observable<MeasurementCategoryModel> {
        return this.http.put<MeasurementCategoryModel>(`${this.apiUrl}/${request.id}`, request);
    }

    deleteCategory(id: number): Observable<boolean> {
        return this.http.delete<boolean>(`${this.apiUrl}/${id}`);
    }

    // Category-specific operations
    getMeasurementsInCategory(categoryId: number): Observable<MeasurementModel[]> {
        return this.http.get<MeasurementModel[]>(`${this.apiUrl}/${categoryId}/measurements`);
    }

    setBaseUnit(categoryId: number, measurementId: number): Observable<MeasurementCategoryModel> {
        return this.http.put<MeasurementCategoryModel>(`${this.apiUrl}/${categoryId}/base-unit`, { measurementId });
    }
}

