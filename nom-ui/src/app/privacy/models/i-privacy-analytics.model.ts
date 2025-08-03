import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface PrivacyAnalyticsModel extends BaseCommonModel {
    complianceScore: number;
    riskScore: number;
    dataProcessingActivities: number;
    consentWithdrawals: number;
    dataExportRequests: number;
    dataDeletionRequests: number;
    auditLogEntries: number;
    lastComplianceCheck: string;
    gdprComplianceStatus: string;
    dataRetentionCompliance: string;
    crossBorderTransfers: number;
    dataBreachIncidents: number;
    privacyImpactAssessments: number;
    userConsentRate: number;
    dataSubjectRightsRequests: number;
    processingPurposes: ProcessingPurposeModel[];
    riskFactors: RiskFactorModel[];
    complianceMetrics: ComplianceMetricModel[];
}

export interface ProcessingPurposeModel {
    purpose: string;
    legalBasis: string;
    dataCategories: string[];
    retentionPeriod: number;
    isActive: boolean;
    consentRate: number;
}

export interface RiskFactorModel {
    factor: string;
    riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
    description: string;
    mitigation: string;
    lastAssessed: string;
}

export interface ComplianceMetricModel {
    metric: string;
    value: number;
    target: number;
    status: 'Compliant' | 'Non-Compliant' | 'At Risk';
    lastUpdated: string;
}

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