import { BaseCommonModel } from '../../common/models/_base-common.model';

/**
 * Model representing a Person's Attribute (e.g., height, weight, activity level).
 */
export class PersonAttributeModel implements BaseCommonModel {
  id: number; // Will be 0 for new attributes
  personId: number; // ID of the person this attribute belongs to
  attributeTypeRefId: number; // Reference to the type of attribute (e.g., from Reference Data)
  value: string; // Stored as string for flexibility (e.g., "180cm", "75kg", "Active")

  constructor(data: any = {}) {
    this.id = data.id || 0;
    this.personId = data.personId || 0;
    this.attributeTypeRefId = data.attributeTypeRefId || 0;
    this.value = data.value || '';
  }
}
