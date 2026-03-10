export interface WebhookUpdateRequest {
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}
