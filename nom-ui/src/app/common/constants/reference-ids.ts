/**
 * Reference Discriminator IDs that match the backend ReferenceDiscriminatorEnum
 * These constants are used throughout the frontend to identify reference groups
 */

export const REFERENCE_IDS = {
    // Core System (1-999)
    MEASUREMENT_TYPE: 1,
    MEAL_TYPE: 1,
    RECIPE_TYPE: 3,

    // Core Application (1000-1999)
    QUESTION_CATEGORY: 1000,

    // Dietary & Health (2000-2999)
    RESTRICTION_TYPE: 2000,

    // Nutritional (3000-3999)
    CUISINE_TYPE: 3001,

    // UI Data Conversion (6000-6999)
    SHOPPING_PRIORITY_TYPE: 6000,
    SHOPPING_CATEGORY_TYPE: 6001,
    RECIPE_DIFFICULTY_TYPE: 6003,
    DAY_OF_WEEK_TYPE: 6015,
    RECIPE_DIETARY_OPTION_TYPE: 6016,

    // Personal Preferences (7000-7999)
    SPICE_LEVEL_TYPE: 7000,
    TEXTURE_TYPE: 7001,
    COOKING_METHOD_TYPE: 7002,
} as const;

/**
 * Type for reference ID values
 */
export type ReferenceId = typeof REFERENCE_IDS[keyof typeof REFERENCE_IDS];

/**
 * Helper function to get reference ID by name
 */
export function getReferenceId(name: keyof typeof REFERENCE_IDS): number {
    return REFERENCE_IDS[name];
}

/**
 * Helper function to check if a number is a valid reference ID
 */
export function isValidReferenceId(id: number): boolean {
    return Object.values(REFERENCE_IDS).includes(id as any);
}
