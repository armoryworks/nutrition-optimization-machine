export interface IRecipeNoteModel {
    id: number;
    recipeId: number;
    recipeName: string;
    authorName: string;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;
    createdDate: string;
    lastModifiedDate?: string;
} 