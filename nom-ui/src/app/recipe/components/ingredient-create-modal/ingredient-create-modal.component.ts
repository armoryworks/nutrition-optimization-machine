// File: nom-ui/src/app/recipe/components/ingredient-create-modal/ingredient-create-modal.component.ts

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

import { IngredientModel } from '../../models/ingredient.model';
import { IngredientFormComponent, IngredientFormData } from '../ingredient-form/ingredient-form.component';

import { IngredientCreateModalData } from './ingredient-create-modal-data.interface';

// Re-export the interface for components that need it
export type { IngredientCreateModalData } from './ingredient-create-modal-data.interface';

@Component({
    selector: 'nom-ingredient-create-modal',
    standalone: true,
    imports: [
        CommonModule,
        MatDialogModule,
        IngredientFormComponent,
    ],
    templateUrl: './ingredient-create-modal.component.html',
    styleUrls: ['./ingredient-create-modal.component.scss']
})
export class IngredientCreateModalComponent implements OnInit {
    private dialogRef = inject<MatDialogRef<IngredientCreateModalComponent>>(MatDialogRef);
    data = inject<IngredientCreateModalData>(MAT_DIALOG_DATA);

    ngOnInit(): void {
        // Component initialization is now handled by the unified form component
    }

    onFormSubmitted(ingredient: IngredientModel): void {
        // Close the dialog with the ingredient result
        this.dialogRef.close(ingredient);
    }

    onFormCancelled(): void {
        // Close the dialog without result
        this.dialogRef.close();
    }

    onDuplicateFound(existingIngredient: IngredientModel): void {
        // Handle duplicate found event if needed
        console.log('Duplicate ingredient found:', existingIngredient);
    }
} 