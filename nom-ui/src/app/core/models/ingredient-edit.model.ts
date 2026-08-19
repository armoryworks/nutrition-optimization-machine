import { IngredientAlias } from './ingredient-alias.model';
import { IngredientNutrient } from './ingredient-nutrient.model';

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
  /** Optional: older API builds omit these; treat missing as empty. */
  aliases?: IngredientAlias[];
  nutrients?: IngredientNutrient[];
}
