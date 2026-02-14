export interface ICookbookResponseModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    slug?: string;
    isPublic: boolean;
    recipeCount: number;
    createdDate: Date;
}

export class CookbookResponseModel implements ICookbookResponseModel {
    id = 0;
    householdId = 0;
    name = '';
    description?: string;
    slug?: string;
    isPublic = false;
    recipeCount = 0;
    createdDate: Date = new Date();

    constructor(data?: Partial<ICookbookResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}
