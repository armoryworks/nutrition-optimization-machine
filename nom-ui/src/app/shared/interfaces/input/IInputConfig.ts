// File: nom-ui/src/app/shared/interfaces/input/IInputConfig.ts

/**
 * Base interface for input component configuration
 */
export interface IInputConfig {
    /**
     * The input label
     */
    label?: string;

    /**
     * The input placeholder
     */
    placeholder?: string;

    /**
     * Whether the input is required
     */
    required?: boolean;

    /**
     * Whether the input is disabled
     */
    disabled?: boolean;

    /**
     * Whether the input is read-only
     */
    readonly?: boolean;

    /**
     * The input type
     */
    type?: string;

    /**
     * The input size
     */
    size?: 'small' | 'medium' | 'large';

    /**
     * The input appearance
     */
    appearance?: 'outline' | 'fill' | 'standard' | 'legacy';

    /**
     * Whether to show the input hint
     */
    showHint?: boolean;

    /**
     * The input hint text
     */
    hint?: string;

    /**
     * Whether to show the input error
     */
    showError?: boolean;

    /**
     * The input error text
     */
    error?: string;

    /**
     * Whether to show the input success state
     */
    showSuccess?: boolean;

    /**
     * The input success text
     */
    success?: string;

    /**
     * Whether to show the input loading state
     */
    loading?: boolean;

    /**
     * Whether to show the input clear button
     */
    showClearButton?: boolean;

    /**
     * Whether to show the input prefix icon
     */
    showPrefixIcon?: boolean;

    /**
     * The input prefix icon
     */
    prefixIcon?: string;

    /**
     * Whether to show the input suffix icon
     */
    showSuffixIcon?: boolean;

    /**
     * The input suffix icon
     */
    suffixIcon?: string;

    /**
     * The input autocomplete
     */
    autocomplete?: string;

    /**
     * The input maxlength
     */
    maxlength?: number;

    /**
     * The input minlength
     */
    minlength?: number;

    /**
     * The input pattern
     */
    pattern?: string;

    /**
     * The input step (for number inputs)
     */
    step?: number;

    /**
     * The input min value
     */
    min?: number;

    /**
     * The input max value
     */
    max?: number;

    /**
     * Custom CSS classes
     */
    cssClasses?: string;

    /**
     * Custom styles
     */
    styles?: { [key: string]: string };

    /**
     * Whether to enable auto-focus
     */
    autofocus?: boolean;

    /**
     * Whether to enable spell check
     */
    spellcheck?: boolean;

    /**
     * Whether to enable auto-capitalize
     */
    autocapitalize?: 'off' | 'none' | 'on' | 'sentences' | 'words' | 'characters';

    /**
     * Whether to enable auto-correct
     */
    autocorrect?: 'on' | 'off';

    /**
     * The input tab index
     */
    tabindex?: number;
} 