import { BaseCommonModel } from '../../common/models/_base-common.model';
import { ProcessingPurposeModel } from './processing-purpose.model';
import { RiskFactorModel } from './risk-factor.model';
import { ComplianceMetricModel } from './compliance-metric.model';

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