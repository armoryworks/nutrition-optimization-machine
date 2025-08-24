export interface IRecipeNoteCreateModel {
    recipeId: number;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;
}

