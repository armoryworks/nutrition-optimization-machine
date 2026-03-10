export interface UserSummary {
  userId: string;
  email: string;
  fullName: string;
  isAdmin: boolean;
  isCurator: boolean;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  createdDate: string;
}
