import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface MessageModel extends BaseCommonModel {
    messageThreadId: number;
    senderPersonId: number;
    content: string;
    timestamp: Date;
    isRead: boolean;
}