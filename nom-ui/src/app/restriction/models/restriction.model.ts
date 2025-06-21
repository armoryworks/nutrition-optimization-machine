import { BaseCommonModel } from '../../common/models/_base-common.model';

/**
 * Model representing a Restriction entity.
 * Used for defining dietary constraints for a person or a plan.
 */
export class RestrictionModel implements BaseCommonModel {
  id: number; // Will be 0 for new restrictions
  personId: number | null; // Nullable if applies to plan, otherwise ID of specific person
  planId: number | null; // Nullable if applies to specific person, otherwise ID of specific plan
  name: string; // The name of the restriction (e.g., "Vegan", "Gluten-Free", "Peanuts")
  description: string | null;
  restrictionTypeId: number; // Reference to the type of restriction (e.g., Dietary Foundation, Allergy)

  // Frontend-only properties for conditional allocation (not part of backend entity)
  appliesToEntirePlan: boolean = false;
  affectedPersonIds: number[] = []; // List of person IDs if AppliesToEntirePlan is false

  constructor(data: any = {}) {
    this.id = data.id || 0;
    this.personId = data.personId || null;
    this.planId = data.planId || null;
    this.name = data.name || '';
    this.description = data.description || null;
    this.restrictionTypeId = data.restrictionTypeId || 0;

    // Initialize frontend-only properties
    this.appliesToEntirePlan = data.appliesToEntirePlan || false;
    this.affectedPersonIds = data.affectedPersonIds
      ? [...data.affectedPersonIds]
      : [];
  }
}
