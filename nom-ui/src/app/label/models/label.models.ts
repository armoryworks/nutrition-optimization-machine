export interface LabelResponseModel {
    id: number;
    name: string;
    color?: string;
    groupName?: string;
}

export interface LabelCreateRequestModel {
    name: string;
    color?: string;
    groupName?: string;
}
