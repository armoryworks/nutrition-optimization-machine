import { RetailPackagingResponse } from './retail-packaging-response.model';

export interface RetailPackagingLookupResponse {
  results: RetailPackagingResponse[];
  notFound: string[];
  aiLookupPerformed: boolean;
}
