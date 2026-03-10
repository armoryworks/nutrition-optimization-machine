export interface UpdateUserClaimsRequest {
  userId: string;
  claims: { type: string; value: string }[];
}
