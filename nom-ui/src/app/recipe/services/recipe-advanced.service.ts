import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { RecipeCommentModel, RecipeCommentCreateModel } from "../models/recipe-comment.model";
import { RecipeRatingModel, RecipeRatingCreateModel } from "../models/recipe-rating.model";
import { RecipeShareTokenModel, RecipeShareTokenCreateModel } from "../models/recipe-share-token.model";
import { RecipeTimelineEventModel, RecipeTimelineEventCreateModel } from "../models/recipe-timeline-event.model";
import { RecipeNoteModel, RecipeNoteCreateModel } from "../models/recipe-note.model";

@Injectable({
    providedIn: "root",
})
export class RecipeAdvancedService {
    private readonly apiUrl = "/api/RecipeAdvanced";

    constructor(private http: HttpClient) { }

    // Comments
    createComment(request: RecipeCommentCreateModel): Observable<RecipeCommentModel> {
        return this.http.post<RecipeCommentModel>(`${this.apiUrl}/comments`, request);
    }

    getRecipeComments(recipeId: number): Observable<RecipeCommentModel[]> {
        return this.http.get<RecipeCommentModel[]>(`${this.apiUrl}/recipes/${recipeId}/comments`);
    }

    deleteComment(commentId: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/comments/${commentId}`);
    }

    // Ratings
    createRating(request: RecipeRatingCreateModel): Observable<RecipeRatingModel> {
        return this.http.post<RecipeRatingModel>(`${this.apiUrl}/ratings`, request);
    }

    getUserRating(recipeId: number): Observable<RecipeRatingModel> {
        return this.http.get<RecipeRatingModel>(`${this.apiUrl}/recipes/${recipeId}/ratings/user`);
    }

    getRecipeAverageRating(recipeId: number): Observable<{ averageRating: number }> {
        return this.http.get<{ averageRating: number }>(`${this.apiUrl}/recipes/${recipeId}/ratings/average`);
    }

    updateRating(ratingId: number, request: RecipeRatingCreateModel): Observable<any> {
        return this.http.put(`${this.apiUrl}/ratings/${ratingId}`, request);
    }

    deleteRating(ratingId: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/ratings/${ratingId}`);
    }

    // Share Tokens
    createShareToken(request: RecipeShareTokenCreateModel): Observable<RecipeShareTokenModel> {
        return this.http.post<RecipeShareTokenModel>(`${this.apiUrl}/share-tokens`, request);
    }

    getRecipeShareTokens(recipeId: number): Observable<RecipeShareTokenModel[]> {
        return this.http.get<RecipeShareTokenModel[]>(`${this.apiUrl}/recipes/${recipeId}/share-tokens`);
    }

    deleteShareToken(shareTokenId: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/share-tokens/${shareTokenId}`);
    }

    getRecipeByShareToken(shareToken: string): Observable<RecipeShareTokenModel> {
        return this.http.get<RecipeShareTokenModel>(`${this.apiUrl}/share-tokens/${shareToken}/recipe`);
    }

    // Timeline Events
    createTimelineEvent(request: RecipeTimelineEventCreateModel): Observable<RecipeTimelineEventModel> {
        return this.http.post<RecipeTimelineEventModel>(`${this.apiUrl}/timeline-events`, request);
    }

    getRecipeTimelineEvents(recipeId: number): Observable<RecipeTimelineEventModel[]> {
        return this.http.get<RecipeTimelineEventModel[]>(`${this.apiUrl}/recipes/${recipeId}/timeline-events`);
    }

    deleteTimelineEvent(eventId: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/timeline-events/${eventId}`);
    }

    // Notes
    createNote(request: RecipeNoteCreateModel): Observable<RecipeNoteModel> {
        return this.http.post<RecipeNoteModel>(`${this.apiUrl}/notes`, request);
    }

    getRecipeNotes(recipeId: number): Observable<RecipeNoteModel[]> {
        return this.http.get<RecipeNoteModel[]>(`${this.apiUrl}/recipes/${recipeId}/notes`);
    }

    updateNote(noteId: number, request: RecipeNoteCreateModel): Observable<any> {
        return this.http.put(`${this.apiUrl}/notes/${noteId}`, request);
    }

    deleteNote(noteId: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/notes/${noteId}`);
    }

    // Recipe Actions
    markRecipeAsMade(recipeId: number): Observable<any> {
        return this.http.post(`${this.apiUrl}/recipes/${recipeId}/mark-as-made`, {});
    }

    getRecipeLastMade(recipeId: number): Observable<{ lastMade: string | null }> {
        return this.http.get<{ lastMade: string | null }>(`${this.apiUrl}/recipes/${recipeId}/last-made`);
    }
} 