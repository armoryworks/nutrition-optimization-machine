// File: nom-ui/src/app/privacy/models/consent.model.ts
export interface ConsentModel {
  consentTypeRefId: number;
  isConsented: boolean;
  name?: string; // Optional: For display in the UI
  description?: string; // Optional: For display in the UI
}
