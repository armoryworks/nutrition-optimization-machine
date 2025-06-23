import { PersonModel } from '../../person/models/person.model'; // Adjust path as needed
import { RestrictionModel } from '../../restriction/models/restriction.model'; // Adjust path as needed

/**
 * Interface representing the structure of a nutritional plan.
 * Used on the frontend for data transfer and display.
 */
export interface IPlanModel {
  id?: number;
  name: string;
  description?: string;
  invitationCode?: string; // Code for inviting others to this plan
  createdByPersonId?: number; // ID of the person who created this plan

  // Optional: Full PersonModel for the creator if needed on frontend
  // createdByPerson?: PersonModel;

  participants?: PersonModel[]; // People associated with this plan
  restrictions?: RestrictionModel[]; // Restrictions applied to this plan (either plan-wide or per-person)
  // Add other properties relevant to a plan (e.g., goals, start/end dates)
}

/**
 * Model class for a nutritional plan, implementing IPlanModel.
 * Provides a constructor for easier instantiation.
 */
export class PlanModel implements IPlanModel {
  id?: number;
  name: string;
  description?: string;
  invitationCode?: string;
  createdByPersonId?: number;
  // createdByPerson?: PersonModel;
  participants?: PersonModel[];
  restrictions?: RestrictionModel[];

  constructor(data: Partial<IPlanModel> = {}) {
    this.id = data.id;
    this.name = data.name || ''; // Ensure name is always provided or defaults
    this.description = data.description;
    this.invitationCode = data.invitationCode;
    this.createdByPersonId = data.createdByPersonId;
    // this.createdByPerson = data.createdByPerson;
    this.participants = data.participants || [];
    this.restrictions = data.restrictions || [];
  }
}
