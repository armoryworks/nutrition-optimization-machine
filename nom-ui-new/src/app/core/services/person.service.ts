import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  PersonModel,
  PersonCreateModel,
  PersonCreateResponseModel,
  UpdatePersonRequest,
  SaveProfileRequest,
  RestrictionRequest,
  OnboardingStateResponse,
  OnboardingCompleteRequest,
  OnboardingCompleteResponse,
} from '../models/person.model';

@Injectable({ providedIn: 'root' })
export class PersonService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Person`;

  upsertPerson(model: PersonCreateModel): Observable<PersonCreateResponseModel> {
    return this.http.post<PersonCreateResponseModel>(this.apiUrl, model);
  }

  getPersonById(id: number): Observable<PersonModel> {
    return this.http.get<PersonModel>(`${this.apiUrl}/${id}`);
  }

  updatePerson(id: number, request: UpdatePersonRequest): Observable<PersonModel> {
    return this.http.put<PersonModel>(`${this.apiUrl}/${id}`, request);
  }

  searchPersons(query: string, limit = 20): Observable<PersonModel[]> {
    const params = new HttpParams().set('query', query).set('limit', limit);
    return this.http.get<PersonModel[]>(`${this.apiUrl}/search`, { params });
  }

  getOnboardingState(personId: number): Observable<OnboardingStateResponse> {
    return this.http.get<OnboardingStateResponse>(`${this.apiUrl}/${personId}/onboarding`);
  }

  completeOnboarding(personId: number, request: OnboardingCompleteRequest): Observable<OnboardingCompleteResponse> {
    return this.http.post<OnboardingCompleteResponse>(`${this.apiUrl}/${personId}/onboarding`, request);
  }

  getCurrentPerson(): Observable<PersonModel> {
    return this.http.get<PersonModel>(`${this.apiUrl}/me`);
  }

  saveProfile(personId: number, request: SaveProfileRequest): Observable<PersonModel> {
    return this.http.put<PersonModel>(`${this.apiUrl}/${personId}/profile`, request);
  }

  saveRestrictions(personId: number, restrictions: RestrictionRequest[]): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${personId}/restrictions`, restrictions);
  }
}
