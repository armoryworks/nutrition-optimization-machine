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

export class RecipeNoteModel {
    id = 0;
    recipeId = 0;
    noteType = '';
    content = '';
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<RecipeNoteModel>) {
        if (data) {
            Object.assign(this, data);
        }
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