export interface ICookbookUpdateRequestModel {
    name?: string;
    description?: string;
    isPublic?: boolean;
}

export class CookbookUpdateRequestModel implements ICookbookUpdateRequestModel {
    name?: string;
    description?: string;
    isPublic?: boolean;

    constructor(data?: Partial<ICookbookUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}
