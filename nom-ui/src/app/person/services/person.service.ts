import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { OnboardingCompleteRequestModel } from '../../onboarding/models/onboarding-complete-request.model';
import { ApiResponseCommonModel } from '../../common/models/api-response-common.model';
import { PersonModel } from '../models/person.model';

@Injectable({
  providedIn: 'root',
})
export class PersonService {
  private readonly apiUrl = '/api/Person';

  constructor(private http: HttpClient) {}

  createPerson(personData: PersonModel): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(this.apiUrl, {
      personName: personData.name,
    });
  }

  /**
   * Submits the complete onboarding data for a user.
   * This includes Person details, attributes, and all collected restrictions.
   */
  submitOnboardingComplete(
    request: OnboardingCompleteRequestModel
  ): Observable<ApiResponseCommonModel> {
    // In a real scenario, you might not send personId from frontend, but derive it from claims on backend.
    // Ensure nested objects are plain JS objects for the API call.
    const apiPayload = {
      personId: request.personId,
      personDetails: {
        name: request.personDetails.name,
        // Map other personDetails properties here
      },
      attributes: request.attributes.map((attr) => ({
        personId: attr.personId,
        attributeTypeRefId: attr.attributeTypeRefId,
        value: attr.value,
      })),
      restrictions: request.restrictions.map((res) => ({
        personId: res.personId,
        planId: res.planId,
        name: res.name,
        description: res.description,
        restrictionTypeId: res.restrictionTypeId,
        appliesToEntirePlan: res.appliesToEntirePlan, // These flags are for backend processing logic
        affectedPersonIds: res.affectedPersonIds, // These IDs help backend map restrictions
      })),
      planInvitationCode: request.planInvitationCode,
      hasAdditionalParticipants: request.hasAdditionalParticipants,
      numberOfAdditionalParticipants: request.numberOfAdditionalParticipants,
      additionalParticipantDetails: request.additionalParticipantDetails.map(
        (p) => ({
          name: p.name,
          // Map other additionalParticipantDetails properties here
        })
      ),
      applyIndividualPreferencesToEachPerson:
        request.applyIndividualPreferencesToEachPerson,
    };

    // --- Actual API Call (Uncomment when ready) ---
    return this.http
      .post<ApiResponseCommonModel>(
        `${this.apiUrl}/onboarding-complete`,
        apiPayload
      )
      .pipe(
        tap((response: any) =>
          console.log('Onboarding complete submission response:', response)
        )
      );
  }
}
