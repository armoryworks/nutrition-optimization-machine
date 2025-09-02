// File: nom-ui/src/app/recipe/models/reference-item.model.ts

export interface ReferenceItemModel {
    id: number;
    name: string;
    symbol?: string;
    // Alternative property names used by some components
    referenceId?: number;
    referenceName?: string;
    referenceDescription?: string;
}