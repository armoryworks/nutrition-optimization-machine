export interface TwoFactorStatus {
  isEnabled: boolean;
  hasAuthenticator: boolean;
  recoveryCodesLeft: number;
}
