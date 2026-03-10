import { PersonAttributeRequest } from './person-attribute-request.model';
import { PersonDetailsRequest } from './person-details-request.model';
import { RestrictionRequest } from './restriction-request.model';

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
