import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';

export interface ShoppingItemDialogData {
    shoppingListId: number;
    mode: 'add' | 'edit';
    item?: any;
}

export interface ShoppingItemFormData {
    name: string;
    description?: string;
    quantity: number;
    unit?: string;
    category?: string;
    priority: 'low' | 'medium' | 'high';
    isCompleted: boolean;
}

@Component({
    selector: 'nom-shopping-item-dialog',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatDialogModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatSelectModule,
        MatCheckboxModule
    ],
    templateUrl: './shopping-item-dialog.component.html',
    styleUrls: ['./shopping-item-dialog.component.scss']
})
export class ShoppingItemDialogComponent implements OnInit {
    itemForm: FormGroup;
    isSubmitting = false;

    categories = [
        'Produce',
        'Dairy',
        'Meat',
        'Pantry',
        'Frozen',
        'Beverages',
        'Snacks',
        'Household',
        'Other'
    ];

    units = [
        'pieces',
        'pounds',
        'ounces',
        'grams',
        'kilograms',
        'cups',
        'tablespoons',
        'teaspoons',
        'liters',
        'milliliters',
        'bottles',
        'cans',
        'boxes',
        'bags'
    ];

    priorities = [
        { value: 'low', label: 'Low' },
        { value: 'medium', label: 'Medium' },
        { value: 'high', label: 'High' }
    ];

    constructor(
        private fb: FormBuilder,
        private dialogRef: MatDialogRef<ShoppingItemDialogComponent>,
        @Inject(MAT_DIALOG_DATA) public data: ShoppingItemDialogData
    ) {
        this.itemForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(255)]],
            description: ['', [Validators.maxLength(1000)]],
            quantity: [1, [Validators.required, Validators.min(0.01)]],
            unit: ['pieces'],
            category: [''],
            priority: ['medium'],
            isCompleted: [false]
        });
    }

    ngOnInit(): void {
        if (this.data.mode === 'edit' && this.data.item) {
            this.itemForm.patchValue(this.data.item);
        }
    }

    onSubmit(): void {
        if (this.itemForm.valid) {
            this.isSubmitting = true;
            const formData: ShoppingItemFormData = this.itemForm.value;

            this.dialogRef.close(formData);
        }
    }

    onCancel(): void {
        this.dialogRef.close();
    }

    getTitle(): string {
        return this.data.mode === 'add' ? 'Add Item' : 'Edit Item';
    }

    getSubmitText(): string {
        return this.data.mode === 'add' ? 'Add' : 'Update';
    }
} 