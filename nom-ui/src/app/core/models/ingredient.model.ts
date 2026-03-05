export interface IngredientSearchResult {
  id: number;
  name: string;
  fdcId?: string;
  matchedAlias?: string;
}

export interface IngredientEditModel {
  id: number;
  name: string;
  description: string;
  pluralName: string;
  fdcId: string | null;
  fdcDataType: string | null;
  curationStatusId: number | null;
  curationStatusName: string | null;
  authorId: number | null;
  labelId: number | null;
  onHand: boolean;
  aliases: IngredientAlias[];
  nutrients: IngredientNutrient[];
}

export interface IngredientAlias {
  id: number;
  name: string;
}

export interface IngredientNutrient {
  id: number;
  nutrientName: string;
  amount: number;
  unitName: string;
}

export interface CreateIngredientRequest {
  name: string;
  description: string;
  pluralName: string;
}

export interface UpdateIngredientRequest {
  id: number;
  name: string;
  description: string;
  pluralName: string;
}
