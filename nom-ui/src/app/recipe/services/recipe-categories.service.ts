import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RecipeCategoryModel } from '../models/i-recipe-category.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeCategoriesService {
    private readonly apiUrl = 'api/recipe/categories';

    constructor(private http: HttpClient) { }

    getAllCategories(): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}`);
    }

    getCategoryById(categoryId: number): Observable<RecipeCategoryModel> {
        return this.http.get<RecipeCategoryModel>(`${this.apiUrl}/${categoryId}`);
    }

    createCategory(category: Partial<RecipeCategoryModel>): Observable<RecipeCategoryModel> {
        return this.http.post<RecipeCategoryModel>(`${this.apiUrl}`, category);
    }

    updateCategory(categoryId: number, category: Partial<RecipeCategoryModel>): Observable<RecipeCategoryModel> {
        return this.http.put<RecipeCategoryModel>(`${this.apiUrl}/${categoryId}`, category);
    }

    deleteCategory(categoryId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${categoryId}`);
    }

    getCategoriesByRecipe(recipeId: number): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/recipe/${recipeId}`);
    }

    addCategoryToRecipe(recipeId: number, categoryId: number): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/recipe/${recipeId}/categories/${categoryId}`, {});
    }

    removeCategoryFromRecipe(recipeId: number, categoryId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/recipe/${recipeId}/categories/${categoryId}`);
    }

    searchCategories(query: string): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/search`, {
            params: { query }
        });
    }

    getPopularCategories(limit: number = 10): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/popular`, {
            params: { limit: limit.toString() }
        });
    }

    getCategoriesByHousehold(householdId: number): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/household/${householdId}`);
    }

    getRootCategories(): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/root`);
    }

    getChildCategories(parentCategoryId: number): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/${parentCategoryId}/children`);
    }

    getCategoryTree(): Observable<RecipeCategoryModel[]> {
        return this.http.get<RecipeCategoryModel[]>(`${this.apiUrl}/tree`);
    }
} 