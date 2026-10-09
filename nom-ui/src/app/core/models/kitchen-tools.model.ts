export type KitchenToolMode = 'hide' | 'warn' | 'ignore';
export type KitchenToolFit = 'ready' | 'substitute' | 'harder' | 'missing';

export interface KitchenTool {
  id: number;
  key: string;
  name: string;
  category: string;
  ownedByDefault: boolean;
  owned: boolean;
  answered: boolean;
}

export interface KitchenSettings {
  mode: KitchenToolMode;
  tools: KitchenTool[];
}

export interface KitchenToolAnswer {
  toolId: number;
  owned: boolean | null;
}

export interface KitchenSettingsUpdate {
  mode?: KitchenToolMode;
  tools?: KitchenToolAnswer[];
}

export interface RecipeToolNeed {
  toolId: number;
  toolName: string;
  fit: KitchenToolFit;
  usingToolName: string | null;
  note: string | null;
}

export interface RecipeToolCheck {
  recipeId: number;
  fit: KitchenToolFit;
  mode: KitchenToolMode;
  inferred: boolean;
  needs: RecipeToolNeed[];
}

export type KitchenToolSuggestionStatus = 'pending' | 'approved' | 'rejected';

export interface KitchenToolSuggestion {
  id: number;
  name: string;
  displayName: string;
  suggestedCategory: string | null;
  seenCount: number;
  status: KitchenToolSuggestionStatus;
  toolId: number | null;
  createdDate: string;
  examples: { recipeId: number; name: string }[];
}

export interface KitchenToolTaggingStatus {
  enabled: boolean;
  model: string | null;
  recipesTagged: number;
  recipesRemaining: number;
  aiToolLinks: number;
  pendingSuggestions: number;
}

export interface KitchenToolTaggingBatchResult {
  seen: number;
  tagged: number;
  toolLinks: number;
  suggestions: number;
}
