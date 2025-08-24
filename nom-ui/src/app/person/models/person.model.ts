import { PersonAttributeModel } from './person-attribute.model';
import { IPersonModel } from './person.interface';

/**
 * Model class for a person, implementing IPersonModel.
 * Provides a constructor for easier instantiation.
 */
export class PersonModel implements IPersonModel {
  id?: number;
  name: string;
  attributes?: PersonAttributeModel[]; // NEW: To hold health attributes for this person

  constructor(data: Partial<IPersonModel> = {}) {
    this.id = data.id || 0; // Default to 0 for new entities
    this.name = data.name || ''; // Ensure name is always provided or defaults
    this.attributes = data.attributes || []; // Initialize attributes as an empty array
  }
}
