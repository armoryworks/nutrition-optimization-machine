import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

import {
    HouseholdCreateRequestModel,
    HouseholdCreateResponseModel,
    HouseholdResponseModel,
    HouseholdUpdateRequestModel,
    HouseholdInviteTokenCreateRequestModel,
    HouseholdInviteTokenResponseModel,
    HouseholdMemberCreateRequestModel,
    HouseholdMemberResponseModel
} from '../models/household.model';

@Injectable({
    providedIn: 'root'
})
export class HouseholdService {
    private apiUrl = `${environment.apiUrl}/household`;

    constructor(private http: HttpClient) { }

    getHouseholds(): Observable<HouseholdResponseModel[]> {
        return this.http.get<HouseholdResponseModel[]>(this.apiUrl);
    }

    createHousehold(request: HouseholdCreateRequestModel): Observable<HouseholdCreateResponseModel> {
        return this.http.post<HouseholdCreateResponseModel>(this.apiUrl, request);
    }

    getHousehold(id: number): Observable<HouseholdResponseModel> {
        return this.http.get<HouseholdResponseModel>(`${this.apiUrl}/${id}`);
    }

    updateHousehold(id: number, request: HouseholdUpdateRequestModel): Observable<HouseholdResponseModel> {
        return this.http.put<HouseholdResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteHousehold(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    createInviteToken(request: HouseholdInviteTokenCreateRequestModel): Observable<HouseholdInviteTokenResponseModel> {
        return this.http.post<HouseholdInviteTokenResponseModel>(`${this.apiUrl}/invite-token`, request);
    }

    addMember(request: HouseholdMemberCreateRequestModel): Observable<HouseholdMemberResponseModel> {
        return this.http.post<HouseholdMemberResponseModel>(`${this.apiUrl}/member`, request);
    }
} 