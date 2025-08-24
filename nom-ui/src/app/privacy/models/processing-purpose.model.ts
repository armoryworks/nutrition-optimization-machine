export interface ProcessingPurposeModel {
    purpose: string;
    legalBasis: string;
    dataCategories: string[];
    retentionPeriod: number;
    isActive: boolean;
    consentRate: number;
}

