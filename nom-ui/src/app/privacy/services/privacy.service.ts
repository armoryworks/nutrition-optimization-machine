// File: nom-ui/src/app/privacy/services/privacy.service.ts

import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponseCommonModel } from '../../common/models/api-response-common.model';
import { UpdateConsentRequest } from '../models/update-consent.request';
import { DataExportRequest } from '../models/data-export.request';
import { PrivacyRequestStatusResponse } from '../models/privacy-request-status.response';
import { DataDeletionRequest } from '../models/data-deletion.request';

@Injectable({
  providedIn: 'root',
})
export class PrivacyService {
  private http = inject(HttpClient);

  private apiUrl = '/api/privacy';

  // Base URL for the privacy controller

  /**
   * Updates the user's consent settings.
   * @param request The payload with the list of consents to update.
   */
  updateConsent(
    request: UpdateConsentRequest
  ): Observable<ApiResponseCommonModel> {
    return this.http.post<ApiResponseCommonModel>(
      `${this.apiUrl}/consent`,
      request
    );
  }

  /**
   * Requests an export of the user's personal data.
   * @param request The payload specifying the export format.
   */
  requestDataExport(
    request: DataExportRequest
  ): Observable<PrivacyRequestStatusResponse> {
    return this.http.post<PrivacyRequestStatusResponse>(
      `${this.apiUrl}/data-export`,
      request
    );
  }

  /**
   * Requests the deletion of the user's account and personal data.
   * @param request The payload confirming the deletion request.
   */
  requestDataDeletion(
    request: DataDeletionRequest
  ): Observable<PrivacyRequestStatusResponse> {
    return this.http.post<PrivacyRequestStatusResponse>(
      `${this.apiUrl}/data-deletion`,
      request
    );
  }
}
