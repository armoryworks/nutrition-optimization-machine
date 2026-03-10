import { ShoppingListResponse } from './shopping-list-response.model';
import { ShoppingListItemResponse } from './shopping-list-item-response.model';

export interface ShoppingListDetailResponse extends ShoppingListResponse {
  items: ShoppingListItemResponse[];
}
