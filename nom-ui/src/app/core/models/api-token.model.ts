export interface ApiToken {
  id: string;
  name: string;
  description?: string | null;
  createdDate: string;
  lastUsedDate?: string | null;
  isActive: boolean;
}

export interface CreatedApiToken extends ApiToken {
  token: string;
}

export interface CreateApiTokenRequest {
  name: string;
  description?: string | null;
}
