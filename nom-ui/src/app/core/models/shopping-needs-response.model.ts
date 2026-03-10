import { ShoppingNeedItem } from './shopping-need-item.model';

export interface ShoppingNeedsResponse {
  householdId: number;
  daysAhead: number;
  fromDate: string;
  toDate: string;
  mealCount: number;
  needs: ShoppingNeedItem[];
}
