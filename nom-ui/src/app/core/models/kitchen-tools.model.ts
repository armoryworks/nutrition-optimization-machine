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
