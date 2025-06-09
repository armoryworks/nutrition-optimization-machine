export interface UpdateTwoFactor {
  enable: boolean;
  twoFactorCode: string;
  resetSharedKey: boolean;
  resetRecoverCodes: boolean;
  forgetMachine: boolean;
}
