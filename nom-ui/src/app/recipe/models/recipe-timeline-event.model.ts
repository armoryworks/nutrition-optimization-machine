export interface IRecipeTimelineEventModel {
    id: number;
    recipeId: number;
    recipeName: string;
    eventTypeId: number;
    eventTypeName: string;
    eventTitle: string;
    eventDescription?: string;
    eventDate?: string;
    createdDate: string;
    lastModifiedDate?: string;
}

export class RecipeTimelineEventModel implements IRecipeTimelineEventModel {
    id: number;
    recipeId: number;
    recipeName: string;
    eventTypeId: number;
    eventTypeName: string;
    eventTitle: string;
    eventDescription?: string;
    eventDate?: string;
    createdDate: string;
    lastModifiedDate?: string;

    constructor(data: Partial<IRecipeTimelineEventModel> = {}) {
        this.id = data.id || 0;
        this.recipeId = data.recipeId || 0;
        this.recipeName = data.recipeName || "";
        this.eventTypeId = data.eventTypeId || 0;
        this.eventTypeName = data.eventTypeName || "";
        this.eventTitle = data.eventTitle || "";
        this.eventDescription = data.eventDescription;
        this.eventDate = data.eventDate;
        this.createdDate = data.createdDate || "";
        this.lastModifiedDate = data.lastModifiedDate;
    }
}

export interface IRecipeTimelineEventCreateModel {
    recipeId: number;
    eventTypeId: number;
    eventTitle: string;
    eventDescription?: string;
    eventDate?: string;
}

export class RecipeTimelineEventCreateModel implements IRecipeTimelineEventCreateModel {
    recipeId: number;
    eventTypeId: number;
    eventTitle: string;
    eventDescription?: string;
    eventDate?: string;

    constructor(data: Partial<IRecipeTimelineEventCreateModel> = {}) {
        this.recipeId = data.recipeId || 0;
        this.eventTypeId = data.eventTypeId || 0;
        this.eventTitle = data.eventTitle || "";
        this.eventDescription = data.eventDescription;
        this.eventDate = data.eventDate;
    }
} 