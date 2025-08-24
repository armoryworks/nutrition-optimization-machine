export interface ComplianceMetricModel {
    metric: string;
    value: number;
    target: number;
    status: 'Compliant' | 'Non-Compliant' | 'At Risk';
    lastUpdated: string;
}

