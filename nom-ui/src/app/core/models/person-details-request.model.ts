import { PersonAttributeRequest } from './person-attribute-request.model';

export interface PersonDetailsRequest {
  id: number;
  name: string;
  attributes: PersonAttributeRequest[];
}
