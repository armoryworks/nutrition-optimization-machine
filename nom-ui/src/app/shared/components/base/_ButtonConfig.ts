// File: nom-ui/src/app/shared/components/base/_ButtonConfig.ts

/**
 * Button configuration interface
 */
export interface ButtonConfig {
    /**
     * Button text
     */
    text?: string;

    /**
     * Button icon (CSS class)
     */
    icon?: string;

    /**
     * Button variant
     */
    variant?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'outline';

    /**
     * Button size
     */
    size?: 'small' | 'medium' | 'large';

    /**
     * Button type
     */
    type?: 'button' | 'submit' | 'reset';

    /**
     * Whether the button is disabled
     */
    disabled?: boolean;

    /**
     * Whether the button is loading
     */
    loading?: boolean;

    /**
     * Whether to show loading spinner
     */
    showLoading?: boolean;

    /**
     * Whether the button takes full width
     */
    fullWidth?: boolean;

    /**
     * Whether the button has rounded corners
     */
    rounded?: boolean;

    /**
     * ARIA label
     */
    ariaLabel?: string;

    /**
     * ARIA described by
     */
    ariaDescribedBy?: string;

    /**
     * Custom CSS classes
     */
    customClasses?: string[];

    /**
     * Custom styles
     */
    customStyles?: Record<string, string>;
} 