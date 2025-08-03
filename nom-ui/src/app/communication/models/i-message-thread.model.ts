import { BaseCommonModel } from '../../common/models/_base-common.model';
import { MessageModel } from './message.model';
import { MessageParticipantModel } from './i-message-participant.model';

export interface MessageThreadModel extends BaseCommonModel {
    participants: MessageParticipantModel[];
    lastMessage?: MessageModel;
    unreadCount: number;
    lastActivity?: string;
    isArchived: boolean;
    isPinned: boolean;
}