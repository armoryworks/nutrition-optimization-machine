export type ScrapingSourceStatus = 'Pending' | 'Approved' | 'Rejected';

export interface ScrapingSourceModel {
  id: number;
  domain: string;
  status: ScrapingSourceStatus;
  sampleUrl?: string;
  requestedByName?: string;
  createdDate: string;
  reviewedByName?: string;
  reviewedDate?: string;
  notes?: string;
}
