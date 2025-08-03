// File: nom-ui/src/app/shared/components/base/_BaseButtonComponent.ts

import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { BehaviorSubject, Subject } from 'rxjs';

/**
 * Abstract base button component that should be extended by concrete implementations
 */
@Component({
    selector: 'nom-base-button',
    templateUrl: './_BaseButtonComponent.html',
    styleUrls: ['./_BaseButtonComponent.scss']
})
export abstract class _BaseButtonComponent implements OnInit, OnDestroy {
    @Input() config: _ButtonConfig = {};
    @Output() click = new EventEmitter<MouseEvent>();
    @Output() focus = new EventEmitter<FocusEvent>();
    @Output() blur = new EventEmitter<FocusEvent>();
    @Output() keyDown = new EventEmitter<KeyboardEvent>();

    protected readonly isDisabled$ = new BehaviorSubject<boolean>(false);
    protected readonly isLoading$ = new BehaviorSubject<boolean>(false);
    protected readonly destroy$ = new Subject<void>();

    ngOnInit(): void {
        this.updateDisabledState();
        this.updateLoadingState();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    /**
     * Handles button click
     */
    onClick(event: MouseEvent): void {
        if (this.isDisabled$.value || this.isLoading$.value) {
            return;
        }

        this.click.emit(event);

        if (this.config.showLoading) {
            this.setLoading(true);
        }
    }

    /**
     * Handles button focus
     */
    onFocus(event: FocusEvent): void {
        this.focus.emit(event);
    }

    /**
     * Handles button blur
     */
    onBlur(event: FocusEvent): void {
        this.blur.emit(event);
    }

    /**
     * Handles key down events
     */
    onKeyDown(event: KeyboardEvent): void {
        this.keyDown.emit(event);

        // Handle Enter and Space keys
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            this.onClick(event as any);
        }
    }

    /**
     * Sets the disabled state
     */
    setDisabled(disabled: boolean): void {
        this.isDisabled$.next(disabled);
    }

    /**
     * Sets the loading state
     */
    setLoading(loading: boolean): void {
        this.isLoading$.next(loading);
    }

    /**
     * Gets the button CSS classes - to be implemented by concrete classes
     */
    abstract getButtonClasses(): string;

    /**
     * Updates the disabled state based on configuration
     */
    private updateDisabledState(): void {
        this.isDisabled$.next(this.config.disabled || false);
    }

    /**
     * Updates the loading state based on configuration
     */
    private updateLoadingState(): void {
        this.isLoading$.next(this.config.loading || false);
    }
}

/**
 * Button configuration interface
 */
export interface _ButtonConfig {
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
    customStyles?: { [key: string]: string };
}

/**
 * Button events interface
 */
export interface _ButtonEvents {
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