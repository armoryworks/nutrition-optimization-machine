// File: nom-ui/src/app/recipe/components/ingredient-edit/ingredient-edit.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { RecipeService } from '../../services/recipe.service';
import { Observable, of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';


@Component({
    selector: 'app-ingredient-edit',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        RouterModule,
        MatProgressSpinnerModule,
        MatIconModule,
        MatCardModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule
    ],
    templateUrl: './ingredient-edit.component.html',
    styleUrls: ['./ingredient-edit.component.scss']
})
export class IngredientEditComponent implements OnInit {
    ingredientForm: FormGroup;
    isEditMode = false;
    ingredientId: number | null = null;
    isLoading = true;
    pageTitle = 'Create New Ingredient';

    constructor(
        private fb: FormBuilder,
        private route: ActivatedRoute,
        public router: Router,
        private recipeService: RecipeService
    ) {
        this.ingredientForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(2047)]],
            description: ['', [Validators.maxLength(4095)]],
            nutrients: this.fb.array([])
        });
    }

    ngOnInit(): void {
        this.route.paramMap.pipe(
            switchMap(params => {
                const id = params.get('id');
                if (id) {
                    this.isEditMode = true;
                    this.pageTitle = 'Edit Ingredient';
                    this.ingredientId = +id;
                    return of(null); // Placeholder for fetch call
                }
                this.isLoading = false;
                return of(null);
            })
        ).subscribe(ingredientData => {
            if (this.isEditMode && ingredientData) {
                // this.ingredientForm.patchValue(ingredientData);
            }
            this.isLoading = false;
        });
    }

    get nutrients(): FormArray {
        return this.ingredientForm.get('nutrients') as FormArray;
    }

    newNutrient(): FormGroup {
        return this.fb.group({
            nutrientId: ['', Validators.required],
            amount: [0, [Validators.required, Validators.min(0)]],
            measurementTypeId: ['', Validators.required]
        });
    }

    addNutrient(): void {
        this.nutrients.push(this.newNutrient());
    }

    removeNutrient(index: number): void {
        this.nutrients.removeAt(index);
    }

    onSubmit(): void {
        if (this.ingredientForm.invalid) {
            this.ingredientForm.markAllAsTouched();
            return;
        }
        console.log('Submitting Ingredient:', this.ingredientForm.value);
        this.router.navigate(['/user/dashboard']);
    }
}