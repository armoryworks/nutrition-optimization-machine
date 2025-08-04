// File: nom-ui/src/app/shared/components/base/_ButtonEvents.ts

/**
 * Button events interface
 */
export interface ButtonEvents {
    /**
     * Click event
     */
    click: MouseEvent;

    /**
     * Focus event
     */
    focus: FocusEvent;

    /**
     * Blur event
     */
    blur: FocusEvent;

    /**
     * Key down event
     */
    keyDown: KeyboardEvent;
} 