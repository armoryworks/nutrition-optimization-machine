export interface IRecipeNoteModel {
    id: number;
    recipeId: number;
    recipeName: string;
    authorId: number;
    authorName: string;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;
    createdDate: string;
    lastModifiedDate?: string;
}

export class RecipeNoteModel implements IRecipeNoteModel {
    id: number;
    recipeId: number;
    recipeName: string;
    authorId: number;
    authorName: string;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;
    createdDate: string;
    lastModifiedDate?: string;

    constructor(data: Partial<IRecipeNoteModel> = {}) {
        this.id = data.id || 0;
        this.recipeId = data.recipeId || 0;
        this.recipeName = data.recipeName || "";
        this.authorId = data.authorId || 0;
        this.authorName = data.authorName || "";
        this.noteTitle = data.noteTitle || "";
        this.noteText = data.noteText;
        this.isPublic = data.isPublic || false;
        this.createdDate = data.createdDate || "";
        this.lastModifiedDate = data.lastModifiedDate;
    }
}

export interface IRecipeNoteCreateModel {
    recipeId: number;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;
}

export class RecipeNoteCreateModel implements IRecipeNoteCreateModel {
    recipeId: number;
    noteTitle: string;
    noteText?: string;
    isPublic: boolean;

    constructor(data: Partial<IRecipeNoteCreateModel> = {}) {
        this.recipeId = data.recipeId || 0;
        this.noteTitle = data.noteTitle || "";
        this.noteText = data.noteText;
        this.isPublic = data.isPublic || false;
    }
} 