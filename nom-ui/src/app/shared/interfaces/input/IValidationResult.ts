/**
 * Base interface for validation results
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

    /**
     * The current value
     */
    value: string | number;
} 