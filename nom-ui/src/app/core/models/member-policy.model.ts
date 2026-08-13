export interface FrequencyCap {
  tag: string;
  maxPerWeek: number;
}

/**
 * Per-member household policy. Feature gates map known gate keys to booleans:
 * an absent key means the feature is allowed; an explicit false means gated.
 */
export interface MemberPolicyModel {
  householdId: number;
  personId: number;
  featureGates: { [key: string]: boolean };
  frequencyCaps: FrequencyCap[];
  curatedOnly: boolean;
  updatedBy: string | null;
}

/** NOM-owned feature gate keys (unknown keys are ignored, not errors). */
export type FeatureGateKey = 'shuffle' | 'recipe_import' | 'recipe_create' | 'recipe_edit';
