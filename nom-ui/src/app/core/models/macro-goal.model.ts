export interface MacroGoal {
  caloriesTarget: number | null;
  proteinGramsTarget: number | null;
  carbGramsTarget: number | null;
  fatGramsTarget: number | null;
}

export interface EffectiveMacroGoal extends MacroGoal {
  /** Where the effective targets came from. */
  source: 'person' | 'household' | 'none';
}
