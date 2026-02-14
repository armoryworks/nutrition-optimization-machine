import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { InvitationModel, CreateInvitationRequest, ClaimInvitationRequest } from '../models/invitation.model';

@Injectable({
  providedIn: 'root',
})
export class InvitationService {
  private http = inject(HttpClient);
  private readonly apiUrl = '/api/Invitation';

  createInvitation(request: CreateInvitationRequest): Observable<InvitationModel> {
    return this.http.post<InvitationModel>(this.apiUrl, request);
  }

  claimInvitation(request: ClaimInvitationRequest): Observable<InvitationModel> {
    return this.http.post<InvitationModel>(`${this.apiUrl}/claim`, request);
  }

  validateInvitationCode(code: string): Observable<boolean> {
    return this.http.get<boolean>(`${this.apiUrl}/validate/${encodeURIComponent(code)}`);
  }

  getInvitationByCode(code: string): Observable<InvitationModel> {
    return this.http.get<InvitationModel>(`${this.apiUrl}/code/${encodeURIComponent(code)}`);
  }

  getInvitationsByInviter(inviterPersonId: number): Observable<InvitationModel[]> {
    return this.http.get<InvitationModel[]>(`${this.apiUrl}/inviter/${inviterPersonId}`);
  }

  getInvitationsByInvitee(inviteePersonId: number): Observable<InvitationModel[]> {
    return this.http.get<InvitationModel[]>(`${this.apiUrl}/invitee/${inviteePersonId}`);
  }
}
