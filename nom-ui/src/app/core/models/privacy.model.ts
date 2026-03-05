export interface ConsentItem {
  consentTypeRefId: number;
  isConsented: boolean;
}

export interface ConsentRequest {
  consents: ConsentItem[];
}

export interface DataExportRequest {
  format: 'json' | 'csv';
}

export interface DataDeletionRequest {
  confirm: boolean;
}
