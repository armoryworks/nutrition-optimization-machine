import { PersonAttributeModel } from './person-attribute.model';

/**
 * Interface representing the structure of a person.
 * Used on the frontend for data transfer and display.
 */
export interface IPersonModel {
    id?: number;
    name: string;
    // Add other properties if needed (e.g., email, date of birth)
    attributes?: PersonAttributeModel[]; // NEW: To hold health attributes for this person
}


