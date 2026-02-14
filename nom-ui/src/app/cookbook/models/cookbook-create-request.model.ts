export interface ICookbookCreateRequestModel {
    householdId: number;
    name: string;
    description?: string;
    isPublic: boolean;
}

export class CookbookCreateRequestModel implements ICookbookCreateRequestModel {
    householdId = 0;
    name = '';
    description?: string;
    isPublic = false;

    constructor(data?: Partial<ICookbookCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}
