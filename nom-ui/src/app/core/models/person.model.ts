export interface PersonModel {
  id: number;
  name: string;
  userId: string | null;
  createdDate: string;
  createdByPersonId: number | null;
  attributes: PersonAttributeModel[];
  planParticipations: PlanParticipantModel[];
}

export interface PersonAttributeModel {
  id: number;
  personId: number;
  attributeTypeId: number;
  attributeTypeName: string;
  value: string;
}

export interface PersonAttributeRequest {
  attributeTypeRefId: number;
  value: string;
}

export interface PersonDetailsRequest {
  id: number;
  name: string;
  attributes: PersonAttributeRequest[];
}

export interface RestrictionRequest {
  name: string;
  description: string | null;
  restrictionTypeId: number;
  appliesToEntirePlan: boolean;
  affectedPersonIds: number[] | null;
}

export interface OnboardingStateResponse {
  hasExistingPerson: boolean;
  personId: number | null;
  personDetails: PersonDetailsRequest;
  attributes: PersonAttributeRequest[];
  restrictions: RestrictionRequest[];
  planInvitationCode: string | null;
  hasAdditionalParticipants: boolean;
  numberOfAdditionalParticipants: number;
  additionalParticipantDetails: PersonDetailsRequest[];
  applyIndividualPreferencesToEachPerson: boolean;
  hasHousehold: boolean;
  currentStep: number;
  isComplete: boolean;
}

export interface OnboardingCompleteRequest {
  personId: number | null;
  personDetails: PersonDetailsRequest;
  attributes: PersonAttributeRequest[];
  restrictions: RestrictionRequest[];
  planInvitationCode: string | null;
  hasAdditionalParticipants: boolean;
  numberOfAdditionalParticipants: number;
  additionalParticipantDetails: PersonDetailsRequest[];
  applyIndividualPreferencesToEachPerson: boolean;
}

export interface OnboardingCompleteResponse {
  success: boolean;
  message: string;
  personId: number;
}

export interface PersonCreateModel {
  personName: string;
}

export interface PersonCreateResponseModel {
  id: number;
  name: string;
  userId: string | null;
}

export interface UpdatePersonRequest {
  id: number;
  name: string;
  userId: string | null;
}

export interface SaveProfileRequest {
  name: string;
  attributes: PersonAttributeRequest[];
  email?: string;
  householdId?: number;
}

export interface PlanParticipantModel {
  id: number;
  personId: number;
  personName: string;
  role: string;
  joinedDate: string;
}
