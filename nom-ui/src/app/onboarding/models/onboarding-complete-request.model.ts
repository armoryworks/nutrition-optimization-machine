import { PersonModel } from '../../person/models/person.model'; // Adjust path as needed
import { PersonAttributeModel } from '../../person/models/person-attribute.model'; // Adjust path as needed
import { RestrictionModel } from '../../restriction/models/restriction.model'; // Adjust path as needed

/**
 * Interface representing the complete data collected during the onboarding process.
 * This is the DTO sent to the backend to finalize onboarding.
 */
export interface IOnboardingCompleteRequestModel {
  personId: number | null; // The real ID of the primary person (will be assigned by backend for new users)
  personDetails: PersonModel; // Details of the primary person (name, etc.)
  attributes: PersonAttributeModel[]; // Health attributes for the primary person (legacy, now nested in PersonModel)
  restrictions: RestrictionModel[]; // All collected restrictions (plan-wide or person-specific)
  planInvitationCode: string | null; // Optional: Code to join an existing plan

  // --- NEW PARTICIPANT-RELATED PROPERTIES ---
  hasAdditionalParticipants: boolean; // Whether the plan includes other people
  numberOfAdditionalParticipants: number; // How many additional people
  additionalParticipantDetails: PersonModel[]; // Details of additional participants

  applyIndividualPreferencesToEachPerson: boolean; // Whether to collect individual preferences for each person

  // --- NEW RESTRICTION SCOPE PROPERTIES (for frontend state management) ---
  // These are client-side state, not necessarily sent directly to backend as separate DTO fields,
  // but influence how `restrictions` array is constructed.
  currentRestrictionScope: 'plan' | 'specific' | null; // Tracks the selected scope during workflow
  currentAffectedPersonIds: number[]; // Tracks selected person IDs for 'specific' scope
}

/**
 * Model class for the OnboardingCompleteRequest.
 * Provides a constructor for easier instantiation and default values.
 */
export class OnboardingCompleteRequestModel
  implements IOnboardingCompleteRequestModel
{
  personId: number | null;
  personDetails: PersonModel;
  attributes: PersonAttributeModel[]; // Note: This will eventually be deprecated in favor of PersonModel.attributes
  restrictions: RestrictionModel[];
  planInvitationCode: string | null;
  hasAdditionalParticipants: boolean;
  numberOfAdditionalParticipants: number;
  additionalParticipantDetails: PersonModel[];
  applyIndividualPreferencesToEachPerson: boolean;

  currentRestrictionScope: 'plan' | 'specific' | null;
  currentAffectedPersonIds: number[];

  constructor(data: Partial<IOnboardingCompleteRequestModel> = {}) {
    this.personId = data.personId ?? null;
    this.personDetails = data.personDetails
      ? new PersonModel(data.personDetails)
      : new PersonModel({ id: -1, name: '' }); // Default primary person
    this.attributes = data.attributes || [];
    this.restrictions = data.restrictions || [];
    this.planInvitationCode = data.planInvitationCode ?? null;

    this.hasAdditionalParticipants = data.hasAdditionalParticipants ?? false;
    this.numberOfAdditionalParticipants =
      data.numberOfAdditionalParticipants ?? 0;
    this.additionalParticipantDetails = data.additionalParticipantDetails
      ? data.additionalParticipantDetails.map((p) => new PersonModel(p))
      : [];

    this.applyIndividualPreferencesToEachPerson =
      data.applyIndividualPreferencesToEachPerson ?? false;

    this.currentRestrictionScope = data.currentRestrictionScope ?? 'specific'; // Default to specific
    this.currentAffectedPersonIds = data.currentAffectedPersonIds ?? []; // Default to empty
  }
}
