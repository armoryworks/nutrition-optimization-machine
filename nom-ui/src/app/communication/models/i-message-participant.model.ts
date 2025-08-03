export interface MessageParticipantModel {
    id: number;
    displayName: string;
    email: string;
    avatarUrl?: string;
    isOnline: boolean;
    lastSeen?: string;
} 