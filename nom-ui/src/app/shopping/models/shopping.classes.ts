// This file has been refactored to follow the one class per file rule.
// Individual classes have been moved to their own files:
// - ShoppingListCategory -> shopping-list-category.model.ts
// - ShoppingListTemplate -> shopping-list-template.model.ts  
// - ShoppingListItemTemplate -> shopping-list-item-template.model.ts

// Re-export the ShoppingListResponseModel from its dedicated file
export { ShoppingListResponseModel } from './shopping-list-response.model'; 