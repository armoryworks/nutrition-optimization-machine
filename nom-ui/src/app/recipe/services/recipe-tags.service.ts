import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RecipeTagModel } from '../models/i-recipe-tag.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeTagsService {
    private http = inject(HttpClient);

    private readonly apiUrl = 'api/recipe/tags';



    getAllTags(): Observable<RecipeTagModel[]> {
        return this.http.get<RecipeTagModel[]>(`${this.apiUrl}`);
    }

    getTagById(tagId: number): Observable<RecipeTagModel> {
        return this.http.get<RecipeTagModel>(`${this.apiUrl}/${tagId}`);
    }

    createTag(tag: Partial<RecipeTagModel>): Observable<RecipeTagModel> {
        return this.http.post<RecipeTagModel>(`${this.apiUrl}`, tag);
    }

    updateTag(tagId: number, tag: Partial<RecipeTagModel>): Observable<RecipeTagModel> {
        return this.http.put<RecipeTagModel>(`${this.apiUrl}/${tagId}`, tag);
    }

    deleteTag(tagId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${tagId}`);
    }

    getTagsByRecipe(recipeId: number): Observable<RecipeTagModel[]> {
        return this.http.get<RecipeTagModel[]>(`${this.apiUrl}/recipe/${recipeId}`);
    }

    addTagToRecipe(recipeId: number, tagId: number): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/recipe/${recipeId}/tags/${tagId}`, {});
    }

    removeTagFromRecipe(recipeId: number, tagId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/recipe/${recipeId}/tags/${tagId}`);
    }

    searchTags(query: string): Observable<RecipeTagModel[]> {
        return this.http.get<RecipeTagModel[]>(`${this.apiUrl}/search`, {
            params: { query }
        });
    }

    getPopularTags(limit = 10): Observable<RecipeTagModel[]> {
        return this.http.get<RecipeTagModel[]>(`${this.apiUrl}/popular`, {
            params: { limit: limit.toString() }
        });
    }

    getTagsByHousehold(householdId: number): Observable<RecipeTagModel[]> {
        return this.http.get<RecipeTagModel[]>(`${this.apiUrl}/household/${householdId}`);
    }
} 