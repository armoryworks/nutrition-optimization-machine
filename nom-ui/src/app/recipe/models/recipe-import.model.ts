export interface RecipeScrapeTestResult {
    url: string;
    title: string;
    description: string;
    image: string;
    ingredients: string[];
    instructions: string[];
    prepTime: string;
    cookTime: string;
    totalTime: string;
    yield: string;
    isValid: boolean;
    errorMessage: string;
}

export interface RecipeCreateResponse {
    id: number;
    name: string;
    description: string;
    authorId: number;
    createdDate: string;
    message: string;
}
