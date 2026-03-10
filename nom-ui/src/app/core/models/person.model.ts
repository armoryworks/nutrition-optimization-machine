import { PersonAttributeModel } from './person-attribute.model';
import { PlanParticipantModel } from './plan-participant.model';

export interface PersonModel {
  id: number;
  name: string;
  userId: string | null;
  createdDate: string;
  createdByPersonId: number | null;
  attributes: PersonAttributeModel[];
  planParticipations: PlanParticipantModel[];
}
