// File: nom-ui/src/app/shared/components/base/_BaseButtonComponent.ts

import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { BehaviorSubject, Subject } from 'rxjs';
import { _ButtonConfig } from './_ButtonConfig';


/**
 * Abstract base button component that should be extended by concrete implementations
 */
@Component({
    selector: 'nom-base-button',
    templateUrl: './_BaseButtonComponent.html',
    styleUrls: ['./_BaseButtonComponent.scss']
})
export abstract class BaseButtonComponent implements OnInit, OnDestroy {
    @Input() config: _ButtonConfig = {};
    @Output() buttonClick = new EventEmitter<MouseEvent>();
    @Output() buttonFocus = new EventEmitter<FocusEvent>();
    @Output() buttonBlur = new EventEmitter<FocusEvent>();
    @Output() buttonKeyDown = new EventEmitter<KeyboardEvent>();

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

        this.buttonClick.emit(event);

        if (this.config.showLoading) {
            this.setLoading(true);
        }
    }

    /**
     * Handles button focus
     */
    onFocus(event: FocusEvent): void {
        this.buttonFocus.emit(event);
    }

    /**
     * Handles button blur
     */
    onBlur(event: FocusEvent): void {
        this.buttonBlur.emit(event);
    }

    /**
     * Handles key down events
     */
    onKeyDown(event: KeyboardEvent): void {
        this.buttonKeyDown.emit(event);

        // Handle Enter and Space keys
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            this.onClick(event as MouseEvent);
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