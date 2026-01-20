import { Component, signal, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AmwInputComponent, AmwButtonComponent } from 'angular-material-wrap';

@Component({
    selector: 'nom-plan-name',
    standalone: true,
    imports: [FormsModule, AmwInputComponent, AmwButtonComponent],
    template: `
        <div class="plan-name">
            <h2 class="plan-name__title">{{ title() }}</h2>
            <div class="plan-name__content">
                <p>{{ message() }}</p>
                <amw-input
                    [(ngModel)]="planName"
                    label="Plan Name"
                    placeholder="Enter plan name"
                    class="plan-name__input">
                </amw-input>
            </div>
            <div class="plan-name__actions">
                <amw-button variant="text" (click)="onCancel()">Cancel</amw-button>
                <amw-button variant="filled" color="primary" (click)="onConfirm()" [disabled]="!planName.trim()">
                    Confirm
                </amw-button>
            </div>
        </div>
    `,
    styles: [`
        .plan-name {
            padding: 24px;
        }
        .plan-name__title {
            margin: 0 0 16px 0;
            font-size: 20px;
            font-weight: 500;
        }
        .plan-name__content {
            margin-bottom: 24px;
        }
        .plan-name__content p {
            margin: 0 0 16px 0;
            color: var(--mat-sys-on-surface-variant);
        }
        .plan-name__input {
            width: 100%;
        }
        .plan-name__actions {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
        }
    `]
})
export class PlanNameComponent {
    // Input signals for data (set by parent via instance)
    title = signal('');
    message = signal('');
    defaultValue = signal('');

    planName = '';

    // Signal-based outputs for dialog communication
    confirmed = signal<string | null>(null);
    cancelled = signal(false);

    constructor() {
        // Set initial planName when defaultValue changes
        effect(() => {
            const value = this.defaultValue();
            if (value) {
                this.planName = value;
            }
        });
    }

    onCancel(): void {
        this.cancelled.set(true);
    }

    onConfirm(): void {
        this.confirmed.set(this.planName);
    }
}
