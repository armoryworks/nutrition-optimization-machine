import { BaseCommonModel } from '../../common/models/_base-common.model';

export class RestrictionModel implements BaseCommonModel {
  id: number;
  personId: number | null; // Null if applies to entire plan
  planId: number | null; // Null if applies to specific person(s)
  restrictionTypeId: number; // Corresponds to RestrictionTypeEnum
  name: string;
  description?: string;
  appliesToEntirePlan: boolean;
  affectedPersonIds: number[]; // List of person IDs if appliesToEntirePlan is false

  // Type-specific properties for Societal/Religious/Ethical Restrictions
  societalReligiousEthicalTypeIds?: number[]; // e.g., Vegan, Kosher IDs
  mandatoryInclusions?: string[]; // e.g., 'Fish', 'Dairy'
  fastingSchedules?: string; // Could be a date range or specific days/times

  // Type-specific properties for Allergy/Medical Restrictions
  allergyMedicalIngredientIds?: string[]; // e.g., 'Peanuts', 'Gluten'
  allergyMedicalConditionIds?: number[]; // e.g., Celiac Disease ID, Diabetes Type 1 ID
  gastrointestinalConditions?: number[]; // IDs of specific GI conditions
  kidneyDiseaseNutrientRestrictions?: string[]; // e.g., 'Sodium', 'Potassium'
  vitaminMineralDeficiencies?: string[]; // e.g., 'Vitamin D', 'Iron'
  prescriptionInteractions?: string;

  // Type-specific properties for Personal Preferences
  personalPreferenceSpiceLevel?: string; // e.g., 'Mild', 'Spicy'
  dislikedIngredients?: string[]; // e.g., 'Cilantro', 'Olives'
  dislikedTextures?: string[]; // e.g., 'Mushy', 'Slimy'
  preferredCookingMethods?: string[]; // e.g., 'Baked', 'Grilled'

  // Audit fields (usually set by backend)
  createdByPersonId?: number;
  createdDate?: Date;

  constructor(data?: Partial<RestrictionModel>) {
    this.id = data?.id || 0;
    this.personId = data?.personId === undefined ? null : data.personId;
    this.planId = data?.planId === undefined ? null : data.planId;
    this.restrictionTypeId = data?.restrictionTypeId || 0;
    this.name = data?.name || '';
    this.description = data?.description || '';
    this.appliesToEntirePlan = data?.appliesToEntirePlan || false;
    this.affectedPersonIds = data?.affectedPersonIds || [];

    // Initialize type-specific arrays to empty arrays if not provided
    this.societalReligiousEthicalTypeIds =
      data?.societalReligiousEthicalTypeIds || [];
    this.mandatoryInclusions = data?.mandatoryInclusions || [];
    this.fastingSchedules = data?.fastingSchedules || '';

    this.allergyMedicalIngredientIds = data?.allergyMedicalIngredientIds || [];
    this.allergyMedicalConditionIds = data?.allergyMedicalConditionIds || [];
    this.gastrointestinalConditions = data?.gastrointestinalConditions || []; // Added
    this.kidneyDiseaseNutrientRestrictions =
      data?.kidneyDiseaseNutrientRestrictions || [];
    this.vitaminMineralDeficiencies = data?.vitaminMineralDeficiencies || [];
    this.prescriptionInteractions = data?.prescriptionInteractions || '';

    this.personalPreferenceSpiceLevel =
      data?.personalPreferenceSpiceLevel || '';
    this.dislikedIngredients = data?.dislikedIngredients || [];
    this.dislikedTextures = data?.dislikedTextures || [];
    this.preferredCookingMethods = data?.preferredCookingMethods || [];

    this.createdByPersonId = data?.createdByPersonId || undefined;
    this.createdDate = data?.createdDate
      ? new Date(data.createdDate)
      : undefined;
  }
}
