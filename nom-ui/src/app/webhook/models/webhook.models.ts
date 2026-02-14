export interface WebhookResponseModel {
    id: number;
    householdId: number;
    name: string;
    url: string;
    eventType?: string;
    isActive: boolean;
    createdDate: Date;
}

export interface WebhookCreateRequestModel {
    householdId: number;
    name: string;
    url: string;
    eventType?: string;
}

export interface WebhookUpdateRequestModel {
    name?: string;
    url?: string;
    eventType?: string;
    isActive?: boolean;
}
