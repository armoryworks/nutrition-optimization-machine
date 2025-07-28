export interface CurationDecisionRequestModel {
    entityId: number;
    entityType: 'Recipe' | 'Ingredient' | 'Plan';
    decisionNotes?: string;
}