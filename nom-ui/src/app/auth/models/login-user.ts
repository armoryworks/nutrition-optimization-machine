export interface LoginUser {
  email: string;
  password: string;
  twoFactorCode: string;
  toFactorRecoveryCode: string;
  //only front end
  rememberMe: boolean | undefined;
}
