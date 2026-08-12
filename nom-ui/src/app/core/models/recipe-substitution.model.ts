/** A change a substitution makes to one instruction step. Matched to steps by stepNumber (1-based, against the step's order). */
export interface RecipeSubstitutionStepEffectModel {
  id: number;
  stepNumber: number;
  alteredDescription: string;
  newTemperatureFahrenheit?: number;
  durationDeltaMinutes?: number;
}

/** A recipe-scoped substitution offered on the recipe page, with its step effects. */
export interface RecipeSubstitutionModel {
  id: number;
  /** The recipe's ORIGINAL ingredient being substituted. */
  ingredientId: number;
  substituteIngredientId: number;
  substituteName: string;
  ratio: number;
  substituteQuantity?: number;
  substituteMeasurementId?: number;
  substituteMeasurement?: string;
  notes?: string;
  isCurated: boolean;
  stepEffects: RecipeSubstitutionStepEffectModel[];
}

/** An optional add-in ingredient offered on the recipe page. */
export interface RecipeAugmentationModel {
  id: number;
  ingredientId: number;
  ingredientName: string;
  quantity?: number;
  measurementId?: number;
  measurement?: string;
  flavorEffect: string;
  instructions?: string;
  insertAfterStepNumber?: number;
  newTemperatureFahrenheit?: number;
  durationDeltaMinutes?: number;
  isCurated: boolean;
}
