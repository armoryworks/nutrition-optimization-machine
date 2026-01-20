// File: nom-ui/src/app/recipe/components/ingredient-create/ingredient-create.component.ts

import { Component, signal, computed } from '@angular/core';

import { IngredientModel } from '../../models/ingredient.model';
import { IngredientFormComponent } from '../ingredient-form/ingredient-form.component';

@Component({
    selector: 'nom-ingredient-create',
    standalone: true,
    imports: [
        IngredientFormComponent
    ],
    templateUrl: './ingredient-create.component.html',
    styleUrls: ['./ingredient-create.component.scss']
})
export class IngredientCreateComponent {
    // Input signals for data (set by parent via instance)
    recipeId = signal<number | undefined>(undefined);
    ingredientName = signal<string | undefined>(undefined);

    // Computed data object for the form component
    modalData = computed(() => ({
        recipeId: this.recipeId(),
        ingredientName: this.ingredientName()
    }));

    // Signal-based outputs for container communication
    confirmed = signal<IngredientModel | null>(null);
    cancelled = signal(false);
    duplicateFound = signal<IngredientModel | null>(null);

    onFormSubmitted(ingredient: IngredientModel): void {
        this.confirmed.set(ingredient);
    }

    onFormCancelled(): void {
        this.cancelled.set(true);
    }

    onDuplicateFound(existingIngredient: IngredientModel): void {
        this.duplicateFound.set(existingIngredient);
        console.log('Duplicate ingredient found:', existingIngredient);
    }
}
