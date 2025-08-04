// File: nom-ui/src/app/shared/components/base/_BaseInputComponent.ts

import { Component, Input, Output, EventEmitter, forwardRef, OnInit, OnDestroy } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR, FormControl, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { IInputConfig } from '../../interfaces/input/IInputConfig';
import { IValidationResult } from '../../interfaces/input/IValidationResult';

/**
 * Abstract base input component that should be extended by concrete implementations
 */
@Component({
    selector: 'nom-base-input',
    templateUrl: './_BaseInputComponent.html',
    styleUrls: ['./_BaseInputComponent.scss'],
    providers: [
        {
            provide: NG_VALUE_ACCESSOR,
            useExisting: forwardRef(() => _BaseInputComponent),
            multi: true
        }
    ]
})
export abstract class BaseInputComponent implements ControlValueAccessor, OnInit, OnDestroy {
    @Input() config: IInputConfig = {};
    @Input() value: any;
    @Input() disabled = false;
    @Input() readonly = false;
    @Input() required = false;
    @Input() placeholder = '';
    @Input() label = '';
    @Input() type = 'text';
    @Input() size: 'small' | 'medium' | 'large' = 'medium';
    @Input() appearance: 'outline' | 'fill' | 'standard' | 'legacy' = 'outline';
    @Input() showHint = false;
    @Input() hint = '';
    @Input() showError = false;
    @Input() error = '';
    @Input() showSuccess = false;
    @Input() success = '';
    @Input() loading = false;
    @Input() showClearButton = false;
    @Input() showPrefixIcon = false;
    @Input() prefixIcon = '';
    @Input() showSuffixIcon = false;
    @Input() suffixIcon = '';
    @Input() autocomplete = '';
    @Input() maxlength?: number;
    @Input() minlength?: number;
    @Input() pattern?: string;
    @Input() step?: number;
    @Input() min?: number;
    @Input() max?: number;
    @Input() cssClasses = '';
    @Input() styles: { [key: string]: string } = {};
    @Input() autofocus = false;
    @Input() spellcheck = false;
    @Input() autocapitalize: 'off' | 'none' | 'on' | 'sentences' | 'words' | 'characters' = 'off';
    @Input() autocorrect: 'on' | 'off' = 'off';
    @Input() tabindex?: number;

    @Output() valueChange = new EventEmitter<any>();
    @Output() focus = new EventEmitter<FocusEvent>();
    @Output() blur = new EventEmitter<FocusEvent>();
    @Output() click = new EventEmitter<MouseEvent>();
    @Output() dblclick = new EventEmitter<MouseEvent>();
    @Output() keydown = new EventEmitter<KeyboardEvent>();
    @Output() keyup = new EventEmitter<KeyboardEvent>();
    @Output() clear = new EventEmitter<void>();
    @Output() validation = new EventEmitter<IValidationResult>();

    protected destroy$ = new Subject<void>();
    protected formControl = new FormControl();
    protected onChange: (value: any) => void = () => { };
    protected onTouched: () => void = () => { };

    ngOnInit(): void {
        this.initializeFormControl();
        this.setupEventListeners();
        this.applyConfig();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    /**
     * Initializes the form control
     */
    protected initializeFormControl(): void {
        const validators = [];

        if (this.required) {
            validators.push(Validators.required);
        }

        if (this.minlength) {
            validators.push(Validators.minLength(this.minlength));
        }

        if (this.maxlength) {
            validators.push(Validators.maxLength(this.maxlength));
        }

        if (this.pattern) {
            validators.push(Validators.pattern(this.pattern));
        }

        if (this.min !== undefined) {
            validators.push(Validators.min(this.min));
        }

        if (this.max !== undefined) {
            validators.push(Validators.max(this.max));
        }

        this.formControl.setValidators(validators);
        this.formControl.setValue(this.value);
        this.formControl.setDisabledState(this.disabled);
    }

    /**
     * Sets up event listeners
     */
    protected setupEventListeners(): void {
        this.formControl.valueChanges
            .pipe(takeUntil(this.destroy$))
            .subscribe(value => {
                this.onChange(value);
                this.valueChange.emit(value);
                this.validateValue(value);
            });
    }

    /**
     * Applies configuration to the component
     */
    protected applyConfig(): void {
        if (this.config) {
            this.label = this.config.label || this.label;
            this.placeholder = this.config.placeholder || this.placeholder;
            this.required = this.config.required || this.required;
            this.disabled = this.config.disabled || this.disabled;
            this.readonly = this.config.readonly || this.readonly;
            this.type = this.config.type || this.type;
            this.size = this.config.size || this.size;
            this.appearance = this.config.appearance || this.appearance;
            this.showHint = this.config.showHint || this.showHint;
            this.hint = this.config.hint || this.hint;
            this.showError = this.config.showError || this.showError;
            this.error = this.config.error || this.error;
            this.success = this.config.success || this.success;
            this.success = this.config.success || this.success;
            this.loading = this.config.loading || this.loading;
            this.showClearButton = this.config.showClearButton || this.showClearButton;
            this.showPrefixIcon = this.config.showPrefixIcon || this.showPrefixIcon;
            this.prefixIcon = this.config.prefixIcon || this.prefixIcon;
            this.showSuffixIcon = this.config.showSuffixIcon || this.showSuffixIcon;
            this.suffixIcon = this.config.suffixIcon || this.suffixIcon;
            this.autocomplete = this.config.autocomplete || this.autocomplete;
            this.maxlength = this.config.maxlength || this.maxlength;
            this.minlength = this.config.minlength || this.minlength;
            this.pattern = this.config.pattern || this.pattern;
            this.step = this.config.step || this.step;
            this.min = this.config.min || this.min;
            this.max = this.config.max || this.max;
            this.cssClasses = this.config.cssClasses || this.cssClasses;
            this.styles = this.config.styles || this.styles;
            this.autofocus = this.config.autofocus || this.autofocus;
            this.spellcheck = this.config.spellcheck || this.spellcheck;
            this.autocapitalize = this.config.autocapitalize || this.autocapitalize;
            this.autocorrect = this.config.autocorrect || this.autocorrect;
            this.tabindex = this.config.tabindex || this.tabindex;
        }
    }

    /**
     * Validates the input value
     */
    protected validateValue(value: any): void {
        const errors: string[] = [];
        const warnings: string[] = [];

        if (this.required && !value) {
            errors.push('This field is required');
        }

        if (this.minlength && value && value.length < this.minlength) {
            errors.push(`Minimum length is ${this.minlength} characters`);
        }

        if (this.maxlength && value && value.length > this.maxlength) {
            errors.push(`Maximum length is ${this.maxlength} characters`);
        }

        if (this.pattern && value && !new RegExp(this.pattern).test(value)) {
            errors.push('Invalid format');
        }

        if (this.min !== undefined && value && value < this.min) {
            errors.push(`Minimum value is ${this.min}`);
        }

        if (this.max !== undefined && value && value > this.max) {
            errors.push(`Maximum value is ${this.max}`);
        }

        const result: IValidationResult = {
            isValid: errors.length === 0,
            errors,
            warnings,
            value
        };

        this.validation.emit(result);
        this.updateErrorState();
    }

    /**
     * Updates the error state
     */
    protected updateErrorState(): void {
        this.showError = this.formControl.invalid && this.formControl.touched;
    }

    /**
     * Gets the error message
     */
    protected getErrorMessage(): string {
        if (!this.formControl.errors) {
            return '';
        }

        const errors = this.formControl.errors;
        const errorMessages: string[] = [];

        if (errors['required']) {
            errorMessages.push('This field is required');
        }

        if (errors['minlength']) {
            errorMessages.push(`Minimum length is ${errors['minlength'].requiredLength} characters`);
        }

        if (errors['maxlength']) {
            errorMessages.push(`Maximum length is ${errors['maxlength'].requiredLength} characters`);
        }

        if (errors['pattern']) {
            errorMessages.push('Invalid format');
        }

        if (errors['min']) {
            errorMessages.push(`Minimum value is ${errors['min'].min}`);
        }

        if (errors['max']) {
            errorMessages.push(`Maximum value is ${errors['max'].max}`);
        }

        return errorMessages.join(', ');
    }

    /**
     * Handles clear button click
     */
    onClear(): void {
        this.formControl.setValue('');
        this.clear.emit();
    }

    /**
     * Handles focus event
     */
    onFocus(event: FocusEvent): void {
        this.focus.emit(event);
        this.onTouched();
    }

    /**
     * Handles blur event
     */
    onBlur(event: FocusEvent): void {
        this.blur.emit(event);
        this.validateValue(this.formControl.value);
    }

    /**
     * Handles click event
     */
    onClick(event: MouseEvent): void {
        this.click.emit(event);
    }

    /**
     * Handles double click event
     */
    onDblClick(event: MouseEvent): void {
        this.dblclick.emit(event);
    }

    /**
     * Handles key down event
     */
    onKeyDown(event: KeyboardEvent): void {
        this.keydown.emit(event);
    }

    /**
     * Handles key up event
     */
    onKeyUp(event: KeyboardEvent): void {
        this.keyup.emit(event);
    }

    // ControlValueAccessor implementation
    writeValue(value: any): void {
        this.formControl.setValue(value);
    }

    registerOnChange(fn: (value: any) => void): void {
        this.onChange = fn;
    }

    registerOnTouched(fn: () => void): void {
        this.onTouched = fn;
    }

    setDisabledState(isDisabled: boolean): void {
        this.disabled = isDisabled;
        this.formControl.setDisabledState(isDisabled);
    }

    /**
     * Gets CSS classes for the input - to be implemented by concrete classes
     */
    abstract getCssClasses(): string;

    /**
     * Gets input styles - to be implemented by concrete classes
     */
    abstract getInputStyles(): { [key: string]: string };
} 