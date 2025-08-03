// File: nom-ui/src/app/shared/interfaces/input/IInputEvents.ts

import { EventEmitter } from '@angular/core';
import { IValidationResult } from './IValidationResult';

/**
 * Base interface for input component events
 */
export interface IInputEvents {
    /**
     * Fired when the input value changes
     */
    valueChange?: EventEmitter<any>;

    /**
     * Fired when the input is focused
     */
    focus?: EventEmitter<FocusEvent>;

    /**
     * Fired when the input is blurred
     */
    blur?: EventEmitter<FocusEvent>;

    /**
     * Fired when the input is clicked
     */
    click?: EventEmitter<MouseEvent>;

    /**
     * Fired when the input is double-clicked
     */
    dblclick?: EventEmitter<MouseEvent>;

    /**
     * Fired when a key is pressed
     */
    keydown?: EventEmitter<KeyboardEvent>;

    /**
     * Fired when a key is released
     */
    keyup?: EventEmitter<KeyboardEvent>;

    /**
     * Fired when the input is cleared
     */
    clear?: EventEmitter<void>;

    /**
     * Fired when the input is validated
     */
    validation?: EventEmitter<IValidationResult>;
} 