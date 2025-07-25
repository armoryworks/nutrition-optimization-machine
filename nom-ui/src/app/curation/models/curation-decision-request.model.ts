export interface CurationDecisionRequestModel {
    entityId: number;
    entityType: 'Recipe' | 'Ingredient';
    decisionNotes: string;
    publicNotes?: string;
}