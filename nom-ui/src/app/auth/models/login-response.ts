export interface LoginResponse {
  tokenType: string;
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}
