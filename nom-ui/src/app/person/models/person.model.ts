import { BaseCommonModel } from '../../common/models/_base-common.model';

/**
 * Model representing a Person entity.
 * Used for collecting primary user details and additional participant details.
 */
export class PersonModel implements BaseCommonModel {
  id: number; // Will be 0 or null for new persons, populated for existing
  name: string;
  // Add other person-specific fields from your PersonEntity here as needed
  // e.g., dateOfBirth: string | null;
  // e.g., gender: string | null;

  constructor(data: any = {}) {
    this.id = data.id || 0; // Initialize with 0 or null for new entities
    this.name = data.name || '';
    // this.dateOfBirth = data.dateOfBirth || null;
    // this.gender = data.gender || null;
  }
}
