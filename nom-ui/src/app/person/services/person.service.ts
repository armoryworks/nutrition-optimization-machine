import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, map } from 'rxjs';
import { OnboardingCompleteRequestModel } from '../../onboarding/models/onboarding-complete-request.model';
import { ApiResponseCommonModel } from '../../common/models/api-response-common.model';
import { PersonCreateModel } from '../models/person-create.model';
import { PersonCreateResponseModel } from '../models/person-create-response.model';
import { PersonModel } from '../models/person.model';

interface UserInfoResponse {
  status: string;
  personId: number;
  userName?: string;
  email?: string;
}

@Injectable({
  providedIn: 'root',
})
export class PersonService {
  private http = inject(HttpClient);

  private readonly apiUrl = '/api/Person';
  private readonly userInfoUrl = '/api/UserInfo';

  /**
   * Gets the current person details for the authenticated user.
   * This is used during onboarding to get the person created during registration.
   */
  getCurrentPerson(): Observable<PersonModel | null> {
    return this.http.get<UserInfoResponse>(`${this.userInfoUrl}/current`).pipe(
      tap(response => console.log('Current user info response:', response)),
      map(response => {
        if (response.status === 'Complete' && response.personId) {
          // Return a PersonModel with the person ID and name
          return new PersonModel({
            id: response.personId,
            name: response.userName || response.email || 'Unknown'
          });
        }
        return null;
      })
    );
  }

  /**
   * Creates a new person record or updates an existing one for the authenticated user.
   * This prevents duplicate records.
   * @param model The person data containing the name.
   */
  upsertPerson(
    model: PersonCreateModel
  ): Observable<PersonCreateResponseModel> {
    // This POST request points to the `UpsertPerson` endpoint on the PersonController.
    return this.http.post<PersonCreateResponseModel>(this.apiUrl, model);
  }

  /**
   * Submits the complete onboarding data for a user.
   * This includes Person details, attributes, and all collected restrictions.
   * Ensures nested objects like PersonModel and RestrictionModel are converted to plain
   * JavaScript objects suitable for API transmission.
   */
  submitOnboardingComplete(
    request: OnboardingCompleteRequestModel
  ): Observable<ApiResponseCommonModel> {
    const apiPayload = {
      personId: request.personId,
      // Map personDetails to include its nested attributes
      personDetails: {
        name: request.personDetails.name,
        // Include attributes for the primary person, if they are nested in personDetails
        attributes:
          request.personDetails.attributes?.map((attr) => ({
            attributeTypeRefId: attr.attributeTypeRefId,
            value: attr.value,
            // personId will be handled by backend or derived from the main personId
          })) || [],
        // Add any other properties from PersonModel if your backend expects them here
        // e.g., gender: request.personDetails.gender,
      },
      // The top-level 'attributes' array in OnboardingCompleteRequestModel
      // is marked as 'legacy, now nested'. If your backend still consumes it
      // as a separate array for the primary person, you can map it here.
      // Otherwise, it might be redundant if attributes are only consumed via `personDetails.attributes`.
      // For safety, let's include it, assuming it refers to the primary person's attributes.
      attributes:
        request.personDetails.attributes?.map((attr) => ({
          personId: request.personId, // Ensure personId is present for each attribute if backend expects it
          attributeTypeRefId: attr.attributeTypeRefId,
          value: attr.value,
        })) || [],

      // Map restrictions (already relatively flat, just ensure properties are present)
      restrictions: request.restrictions.map((res) => ({
        personId: res.personId,
        planId: res.planId,
        name: res.name,
        description: res.description,
        restrictionTypeId: res.restrictionTypeId,
        appliesToEntirePlan: res.appliesToEntirePlan,
        affectedPersonIds: res.affectedPersonIds,
        // Include any other properties of RestrictionModel if needed
      })),

      planInvitationCode: request.planInvitationCode,
      hasAdditionalParticipants: request.hasAdditionalParticipants,
      numberOfAdditionalParticipants: request.numberOfAdditionalParticipants,

      // Map additionalParticipantDetails to include their nested attributes
      additionalParticipantDetails: request.additionalParticipantDetails.map(
        (p) => ({
          // Important: Backend needs ID for existing people, or null/temp for new ones
          id: p.id, // Include temporary IDs, backend should handle generating real ones
          name: p.name,
          // Include attributes for each additional participant
          attributes:
            p.attributes?.map((attr) => ({
              attributeTypeRefId: attr.attributeTypeRefId,
              value: attr.value,
              // personId will be handled by backend for these attributes
            })) || [],
          // Add any other properties from PersonModel if your backend expects them here
        })
      ),
      applyIndividualPreferencesToEachPerson:
        request.applyIndividualPreferencesToEachPerson,

      // These frontend-only state properties should generally NOT be sent to the backend
      // currentRestrictionScope: request.currentRestrictionScope,
      // currentAffectedPersonIds: request.currentAffectedPersonIds,
    };

    return this.http
      .post<ApiResponseCommonModel>(
        `${this.apiUrl}/onboarding-complete`,
        apiPayload
      )
      .pipe(
        tap((response) =>
          console.log('Onboarding complete submission response:', response)
        )
      );
  }
}
