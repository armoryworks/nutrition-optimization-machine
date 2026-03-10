import { GoalItemModel } from './goal-item.model';

export interface GoalModel {
  id: number;
  name: string;
  description: string;
  goalType: string | null;
  beginDate: string | null;
  endDate: string | null;
  goalItems: GoalItemModel[];
}
