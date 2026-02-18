export interface ReferenceItem {
  referenceId: number;
  referenceName: string;
  referenceDescription: string | null;
  groupId: number;
  groupName: string;
  groupDescription: string | null;
}

export enum ReferenceDiscriminator {
  RestrictionType = 2000,
  GoalType = 2001,
  PersonActivityLevelType = 6004,
  PersonDietaryRestrictionType = 6005,
  PersonHealthGoalType = 6006,
  AllergyType = 6007,
  MedicalConditionType = 6008,
  SocietalRestrictionType = 6009,
  PersonalPreferenceType = 6010,
  PersonAttributeType = 6011,
}
