import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  HouseholdResponseModel,
  HouseholdCreateModel,
  HouseholdCreateResponseModel,
  HouseholdUpdateModel,
  HouseholdMemberResponseModel,
  HouseholdMemberCreateModel,
  HouseholdInviteTokenCreateModel,
  HouseholdInviteTokenResponseModel,
  JoinHouseholdRequest,
} from '../models/household.model';

@Injectable({ providedIn: 'root' })
export class HouseholdService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Household`;

  getHouseholds(): Observable<HouseholdResponseModel[]> {
    return this.http.get<HouseholdResponseModel[]>(this.apiUrl);
  }

  createHousehold(model: HouseholdCreateModel): Observable<HouseholdCreateResponseModel> {
    return this.http.post<HouseholdCreateResponseModel>(this.apiUrl, model);
  }

  getHousehold(id: number): Observable<HouseholdResponseModel> {
    return this.http.get<HouseholdResponseModel>(`${this.apiUrl}/${id}`);
  }

  updateHousehold(id: number, model: HouseholdUpdateModel): Observable<HouseholdResponseModel> {
    return this.http.put<HouseholdResponseModel>(`${this.apiUrl}/${id}`, model);
  }

  deleteHousehold(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  createInviteToken(model: HouseholdInviteTokenCreateModel): Observable<HouseholdInviteTokenResponseModel> {
    return this.http.post<HouseholdInviteTokenResponseModel>(`${this.apiUrl}/invite-token`, model);
  }

  addMember(model: HouseholdMemberCreateModel): Observable<HouseholdMemberResponseModel> {
    return this.http.post<HouseholdMemberResponseModel>(`${this.apiUrl}/member`, model);
  }

  removeMember(householdId: number, memberId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${householdId}/member/${memberId}`);
  }

  joinHousehold(token: string): Observable<HouseholdMemberResponseModel> {
    const request: JoinHouseholdRequest = { token };
    return this.http.post<HouseholdMemberResponseModel>(`${this.apiUrl}/join`, request);
  }
}
