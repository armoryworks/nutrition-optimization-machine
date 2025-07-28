export interface CurationQueueItemModel {
    id: number;
    entityType: 'Recipe' | 'Ingredient' | 'Plan';
    name: string;
    authorName: string;
    dateSubmitted: Date;
    description?: string;
    instructions?: string; // For recipes
    rawIngredientsString?: string; // For recipes
    sourceUrl?: string;
    authorId: number;
} 