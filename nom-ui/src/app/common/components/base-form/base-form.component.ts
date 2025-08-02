import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormGroup } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

export interface BaseFormConfig {
    title: string;
    subtitle?: string;
    submitText?: string;
    cancelText?: string;
    showCancelButton?: boolean;
    showDeleteButton?: boolean;
    deleteText?: string;
    maxWidth?: string;
}

@Component({
    selector: 'app-base-form',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
    ],
    templateUrl: './base-form.component.html',
    styleUrls: ['./base-form.component.scss']
})
export class BaseFormComponent {
    @Input() config?: BaseFormConfig;
    @Input() form!: FormGroup;
    @Input() isSubmitting = false;

    @Output() submit = new EventEmitter<void>();
    @Output() cancel = new EventEmitter<void>();
    @Output() delete = new EventEmitter<void>();

    onSubmit(): void {
        if (this.form.valid && !this.isSubmitting) {
            this.submit.emit();
        }
    }

    onCancel(): void {
        this.cancel.emit();
    }

    onDelete(): void {
        this.delete.emit();
    }
} 