export interface WebhookCreateRequest {
  householdId: number;
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}
