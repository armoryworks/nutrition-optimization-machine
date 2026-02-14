export interface InvitationModel {
    id: number;
    code: string;
    inviterPersonId: number;
    inviterName: string;
    inviteePersonId?: number;
    inviteeName?: string;
    expirationDate?: string;
    isUsed: boolean;
    usedAt?: string;
    notes?: string;
    invitationType: string;
    planId?: number;
    planName?: string;
    createdDate: string;
}

export interface CreateInvitationRequest {
    invitationType: string;
    planId?: number;
    expirationDate?: string;
    notes?: string;
}

export interface ClaimInvitationRequest {
    invitationCode: string;
    inviteePersonId: number;
}
