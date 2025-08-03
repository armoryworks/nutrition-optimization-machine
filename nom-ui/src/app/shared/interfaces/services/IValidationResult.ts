// File: nom-ui/src/app/shared/interfaces/services/IValidationResult.ts

/**
 * Validation result interface
 */
export interface IValidationResult {
    /**
     * Whether the validation passed
     */
    isValid: boolean;

    /**
     * Validation errors
     */
    errors: string[];

    /**
     * Validation warnings
     */
    warnings: string[];
} 