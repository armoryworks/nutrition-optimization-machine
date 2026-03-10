import { PersonAttributeRequest } from './person-attribute-request.model';

export interface SaveProfileRequest {
  name: string;
  attributes: PersonAttributeRequest[];
  email?: string;
  householdId?: number;
}
