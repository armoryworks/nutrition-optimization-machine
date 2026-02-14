import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
    UserResponseModel,
    CreateUserRequestModel,
    UpdateUserRequestModel,
    UserRatingResponseModel,
    ApiTokenResponseModel,
    CreateApiTokenRequestModel,
    ChangePasswordRequest,
} from '../models/user.models';

@Injectable({
    providedIn: 'root'
})
export class UserService {
    private http = inject(HttpClient);

    private readonly apiUrl = '/api/User';

    getCurrentUser(): Observable<UserResponseModel> {
        return this.http.get<UserResponseModel>(`${this.apiUrl}/self`);
    }

    getUserById(userId: string): Observable<UserResponseModel> {
        return this.http.get<UserResponseModel>(`${this.apiUrl}/${userId}`);
    }

    getAllUsers(): Observable<UserResponseModel[]> {
        return this.http.get<UserResponseModel[]>(this.apiUrl);
    }

    createUser(request: CreateUserRequestModel): Observable<UserResponseModel> {
        return this.http.post<UserResponseModel>(this.apiUrl, request);
    }

    updateUser(userId: string, request: UpdateUserRequestModel): Observable<UserResponseModel> {
        return this.http.put<UserResponseModel>(`${this.apiUrl}/${userId}`, request);
    }

    deleteUser(userId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${userId}`);
    }

    getUserRatings(): Observable<UserRatingResponseModel[]> {
        return this.http.get<UserRatingResponseModel[]>(`${this.apiUrl}/self/ratings`);
    }

    getUserRatingForRecipe(recipeId: number): Observable<UserRatingResponseModel> {
        return this.http.get<UserRatingResponseModel>(`${this.apiUrl}/self/ratings/${recipeId}`);
    }

    getUserFavorites(): Observable<UserRatingResponseModel[]> {
        return this.http.get<UserRatingResponseModel[]>(`${this.apiUrl}/self/favorites`);
    }

    changePassword(request: ChangePasswordRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/password`, request);
    }

    uploadUserImage(imageFile: File): Observable<string> {
        const formData = new FormData();
        formData.append('image', imageFile);
        return this.http.post<string>(`${this.apiUrl}/image`, formData);
    }

    deleteUserImage(): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/image`);
    }

    getApiTokens(): Observable<ApiTokenResponseModel[]> {
        return this.http.get<ApiTokenResponseModel[]>(`${this.apiUrl}/api-tokens`);
    }

    createApiToken(request: CreateApiTokenRequestModel): Observable<ApiTokenResponseModel> {
        return this.http.post<ApiTokenResponseModel>(`${this.apiUrl}/api-tokens`, request);
    }

    deleteApiToken(tokenId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/api-tokens/${tokenId}`);
    }
}
