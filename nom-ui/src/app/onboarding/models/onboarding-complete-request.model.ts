import { PersonModel } from '../../person/models/person.model';
import { PersonAttributeModel } from '../../person/models/person-attribute.model';
import { RestrictionModel } from '../../restriction/models/restriction.model';

/**
 * Consolidated model for the complete onboarding submission to the backend.
 * Contains all data collected across various onboarding steps.
 */
export class OnboardingCompleteRequestModel {
  personId: number; // The ID of the primary user completing onboarding (from auth)
  personDetails: PersonModel; // Core details of the primary user
  attributes: PersonAttributeModel[]; // Health attributes for the primary user
  restrictions: RestrictionModel[]; // All restrictions (primary user, additional persons, or plan-wide)
  planInvitationCode: string | null; // Optional invitation code

  hasAdditionalParticipants: boolean; // From UI-only step 5
  numberOfAdditionalParticipants: number; // From UI-only step 6
  additionalParticipantDetails: PersonModel[]; // Names from UI-only step 7 (blank slots)
  applyIndividualPreferencesToEachPerson: boolean; // From UI-only step 8

  constructor(data: any = {}) {
    this.personId = data.personId || 0;
    this.personDetails = data.personDetails
      ? new PersonModel(data.personDetails)
      : new PersonModel();
    this.attributes = data.attributes
      ? data.attributes.map((attr: any) => new PersonAttributeModel(attr))
      : [];
    this.restrictions = data.restrictions
      ? data.restrictions.map((res: any) => new RestrictionModel(res))
      : [];
    this.planInvitationCode = data.planInvitationCode || null;

    this.hasAdditionalParticipants = data.hasAdditionalParticipants || false;
    this.numberOfAdditionalParticipants =
      data.numberOfAdditionalParticipants || 0;
    this.additionalParticipantDetails = data.additionalParticipantDetails
      ? data.additionalParticipantDetails.map((p: any) => new PersonModel(p))
      : [];
    this.applyIndividualPreferencesToEachPerson =
      data.applyIndividualPreferencesToEachPerson || false;
  }
}
