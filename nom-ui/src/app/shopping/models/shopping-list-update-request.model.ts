export interface ShoppingListUpdateRequest {
    name?: string;
    description?: string;
    householdId?: number;
    isActive?: boolean;
    allowMultipleItems?: boolean;
    enableNotifications?: boolean;
} 