import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface DataProcessingLogModel extends BaseCommonModel {
    actionType: string;
    actorId: number;
    timestamp: string;
    details: string;
    ipAddress: string;
    userAgent: string;
    personId: number;
    dataCategories: string[];
    legalBasis: string;
    purpose: string;
}

