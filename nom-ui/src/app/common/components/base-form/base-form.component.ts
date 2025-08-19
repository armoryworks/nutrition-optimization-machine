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
    selector: 'nom-base-form',
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
    @Input() loading = false;

    @Output() formSubmit = new EventEmitter<void>();
    @Output() formCancel = new EventEmitter<void>();
    @Output() formDelete = new EventEmitter<void>();

    onSubmit(): void {
        if (this.form.valid && !this.isSubmitting) {
            this.formSubmit.emit();
        }
    }

    onCancel(): void {
        this.formCancel.emit();
    }

    onDelete(): void {
        this.formDelete.emit();
    }
} 