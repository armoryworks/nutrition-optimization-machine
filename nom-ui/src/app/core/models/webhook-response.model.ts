export interface WebhookResponse {
  id: number;
  householdId: number;
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}
