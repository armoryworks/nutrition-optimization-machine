export interface UserResponseModel {
    id: string;
    username: string;
    email: string;
    fullName?: string;
    image?: string;
    isActive: boolean;
    createdDate: string;
    lastLoginDate?: string;
    emailConfirmed: boolean;
    twoFactorEnabled: boolean;
    canInvite: boolean;
    canManage: boolean;
    canManageHousehold: boolean;
    canOrganize: boolean;
    isAdmin: boolean;
    groupId?: number;
    groupName?: string;
    householdId?: number;
    householdName?: string;
    recipeCount: number;
    ratingCount: number;
    favoriteCount: number;
}

export interface CreateUserRequestModel {
    email: string;
    username: string;
    password: string;
    fullName?: string;
    groupId?: number;
    householdId?: number;
    canInvite: boolean;
    canManage: boolean;
    canManageHousehold: boolean;
    canOrganize: boolean;
    isAdmin: boolean;
}

export interface UpdateUserRequestModel {
    email?: string;
    username?: string;
    fullName?: string;
    groupId?: number;
    householdId?: number;
    canInvite?: boolean;
    canManage?: boolean;
    canManageHousehold?: boolean;
    canOrganize?: boolean;
    isAdmin?: boolean;
}

export interface UserRatingResponseModel {
    id: number;
    recipeId: number;
    recipeName: string;
    recipeImage?: string;
    rating: number;
    comment?: string;
    createdDate: string;
    isFavorite: boolean;
}

export interface ApiTokenResponseModel {
    id: number;
    name: string;
    token: string;
    createdDate: string;
    lastUsedDate?: string;
    isActive: boolean;
}

export interface CreateApiTokenRequestModel {
    name: string;
    description?: string;
}

export interface ChangePasswordRequest {
    currentPassword: string;
    newPassword: string;
}
