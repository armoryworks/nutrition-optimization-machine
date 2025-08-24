export interface RiskFactorModel {
    factor: string;
    riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
    description: string;
    mitigation: string;
    lastAssessed: string;
}

