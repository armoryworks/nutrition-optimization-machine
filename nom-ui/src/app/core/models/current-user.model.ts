/** Subset of GET /api/User/self used for UI gating. */
export interface CurrentUserModel {
  id: string;
  email: string;
  isAdmin: boolean;
  canManage: boolean;
}
