export interface UserModel {
    id: string; // ASP.NET Core Identity UserId is a string
    userName: string;
    email: string;
    canManageCuration: boolean;
    canManageUserRoles: boolean;
}