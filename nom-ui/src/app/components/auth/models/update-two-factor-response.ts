export interface UpdateTwoFactorResponse {
  sharedKey: string;
  recoveryCodesLeft: number;
  recoverCodes: string[];
  isTwoFactorEnabled: boolean;
  isMachineRemembered: boolean;
}
