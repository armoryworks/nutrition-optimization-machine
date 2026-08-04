export interface AdminUser {
  id: string;
  username: string;
  email: string;
  fullName?: string;
  isActive: boolean;
  createdDate: string;
  lastLoginDate?: string;
  emailConfirmed: boolean;
  isAdmin: boolean;
  householdId?: number;
  householdName?: string;
  recipeCount: number;
}

export interface UserClaims {
  userId: string;
  canManageCuration: boolean;
  canManageUserRoles: boolean;
}
