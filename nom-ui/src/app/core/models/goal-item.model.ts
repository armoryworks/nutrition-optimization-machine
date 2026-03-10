export interface GoalItemModel {
  id: number;
  name: string;
  description: string;
  isQuantifiable: boolean;
  ingredientName: string | null;
  nutrientName: string | null;
  timeframeType: string | null;
  measurement: string | null;
  measurementMinimum: number | null;
  measurementMaximum: number | null;
}
