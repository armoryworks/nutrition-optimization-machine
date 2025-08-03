// File: nom-ui/src/app/shared/components/buttons/ButtonComponent.ts

import { Component } from '@angular/core';
import { _BaseButtonComponent } from '../base/_BaseButtonComponent';

/**
 * Concrete button component implementation
 */
@Component({
    selector: 'nom-button',
    templateUrl: './ButtonComponent.html',
    styleUrls: ['./ButtonComponent.scss']
})
export class ButtonComponent extends _BaseButtonComponent {

    /**
     * Gets the button CSS classes
     */
    getButtonClasses(): string {
        const classes = ['base-button'];

        if (this.config.variant) {
            classes.push(this.config.variant);
        }

        if (this.config.size) {
            classes.push(this.config.size);
        }

        if (this.config.fullWidth) {
            classes.push('full-width');
        }

        if (this.config.rounded) {
            classes.push('rounded');
        }

        if (this.isDisabled$.value) {
            classes.push('disabled');
        }

        if (this.isLoading$.value) {
            classes.push('loading');
        }

        return classes.join(' ');
    }
} 