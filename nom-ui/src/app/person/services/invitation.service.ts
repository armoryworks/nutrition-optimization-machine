import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * Service for managing invitation operations.
 * Handles invitation code validation and retrieval.
 */
@Injectable({
  providedIn: 'root',
})
export class InvitationService {
  private http = inject(HttpClient);
  private readonly apiUrl = '/api/Invitation';

  /**
   * Validates an invitation code.
   * Checks if the code exists, is not used, and has not expired.
   *
   * @param code The invitation code to validate
   * @returns Observable<boolean> True if the code is valid and can be used, false otherwise
   */
  validateInvitationCode(code: string): Observable<boolean> {
    return this.http.get<boolean>(`${this.apiUrl}/validate/${encodeURIComponent(code)}`);
  }

  /**
   * Gets invitation details by code.
   * Returns the full invitation object including inviter information.
   *
   * @param code The invitation code
   * @returns Observable<InvitationModel | null> The invitation details or null if not found
   */
  getInvitationByCode(code: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/code/${encodeURIComponent(code)}`);
  }
}
