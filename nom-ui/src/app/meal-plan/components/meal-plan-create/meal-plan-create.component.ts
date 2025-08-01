import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { MealPlanService } from '../../services/meal-plan.service';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { MealPlanCreateRequestModel } from '../../models/meal-plan.model';

@Component({
    selector: 'app-meal-plan-create',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        MatButtonModule,
        MatIconModule,
        MatDatepickerModule,
        MatNativeDateModule,
    ],
    template: `
    <div class="meal-plan-create">
      <div class="meal-plan-create__container">
        <mat-card class="meal-plan-create__card">
          <mat-card-header class="meal-plan-create__header">
            <div class="meal-plan-create__title-section">
              <mat-icon class="meal-plan-create__title-icon">schedule</mat-icon>
              <div class="meal-plan-create__title-info">
                <h1 class="meal-plan-create__title">Create Meal Plan</h1>
                <p class="meal-plan-create__subtitle">Plan your meals for the week</p>
              </div>
            </div>
          </mat-card-header>

          <mat-card-content class="meal-plan-create__content">
            <form [formGroup]="mealPlanForm" (ngSubmit)="onSubmit()" class="meal-plan-create__form">
              <div class="meal-plan-create__form-row">
                <mat-form-field class="meal-plan-create__field">
                  <mat-label>Date</mat-label>
                  <input matInput [matDatepicker]="picker" formControlName="date" required>
                  <mat-datepicker-toggle matSuffix [for]="picker"></mat-datepicker-toggle>
                  <mat-datepicker #picker></mat-datepicker>
                  <mat-error *ngIf="mealPlanForm.get('date')?.hasError('required')">
                    Date is required
                  </mat-error>
                </mat-form-field>

                <mat-form-field class="meal-plan-create__field">
                  <mat-label>Meal Type</mat-label>
                  <mat-select formControlName="mealType" required>
                    <mat-option value="Breakfast">Breakfast</mat-option>
                    <mat-option value="Lunch">Lunch</mat-option>
                    <mat-option value="Dinner">Dinner</mat-option>
                    <mat-option value="Snack">Snack</mat-option>
                  </mat-select>
                  <mat-error *ngIf="mealPlanForm.get('mealType')?.hasError('required')">
                    Meal type is required
                  </mat-error>
                </mat-form-field>
              </div>

              <mat-form-field class="meal-plan-create__field">
                <mat-label>Recipe</mat-label>
                <mat-select formControlName="recipeId">
                  <mat-option value="">No recipe selected</mat-option>
                  <mat-option *ngFor="let recipe of recipes" [value]="recipe.id">
                    {{ recipe.name }}
                  </mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field class="meal-plan-create__field">
                <mat-label>Notes</mat-label>
                <textarea matInput formControlName="notes" rows="3" 
                          placeholder="Add any notes about this meal..."></textarea>
              </mat-form-field>

              <div class="meal-plan-create__actions">
                <button mat-button type="button" (click)="onCancel()" class="meal-plan-create__cancel-button">
                  <mat-icon>cancel</mat-icon>
                  Cancel
                </button>
                <button mat-raised-button color="primary" type="submit" 
                        [disabled]="mealPlanForm.invalid || isSubmitting" class="meal-plan-create__submit-button">
                  <mat-icon>save</mat-icon>
                  {{ isSubmitting ? 'Creating...' : 'Create Meal Plan' }}
                </button>
              </div>
            </form>
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
    styles: [`
    .meal-plan-create {
      min-height: 100vh;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      padding: 2rem 1rem;

      @media (max-width: 768px) {
        padding: 1rem 0.5rem;
      }

      &__container {
        max-width: 600px;
        margin: 0 auto;
      }

      &__card {
        background: rgba(255, 255, 255, 0.95);
        backdrop-filter: blur(10px);
        border-radius: 16px;
        box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
        border: 1px solid rgba(255, 255, 255, 0.2);
        overflow: hidden;
      }

      &__header {
        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
        color: white;
        padding: 2rem 2rem 1.5rem;

        @media (max-width: 768px) {
          padding: 1.5rem 1rem 1rem;
        }
      }

      &__title-section {
        display: flex;
        align-items: center;
        gap: 0.75rem;
      }

      &__title-icon {
        font-size: 2rem;
        width: 2rem;
        height: 2rem;

        @media (max-width: 768px) {
          font-size: 1.75rem;
          width: 1.75rem;
          height: 1.75rem;
        }
      }

      &__title {
        font-size: 1.75rem;
        font-weight: 600;
        margin: 0;
        color: white;

        @media (max-width: 768px) {
          font-size: 1.5rem;
        }
      }

      &__subtitle {
        color: rgba(255, 255, 255, 0.9);
        font-size: 1rem;
        margin: 0.5rem 0 0;
        line-height: 1.5;
      }

      &__content {
        padding: 2rem;

        @media (max-width: 768px) {
          padding: 1.5rem 1rem;
        }
      }

      &__form {
        display: flex;
        flex-direction: column;
        gap: 1.5rem;
      }

      &__form-row {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 1rem;

        @media (max-width: 768px) {
          grid-template-columns: 1fr;
        }
      }

      &__field {
        width: 100%;
      }

      &__actions {
        display: flex;
        justify-content: flex-end;
        gap: 1rem;
        margin-top: 2rem;

        @media (max-width: 768px) {
          flex-direction: column;
        }
      }

      &__cancel-button {
        color: #666;
      }

      &__submit-button {
        min-width: 160px;
      }
    }
  `]
})
export class MealPlanCreateComponent implements OnInit {
    mealPlanForm: FormGroup;
    isSubmitting = false;
    recipes: any[] = [];

    constructor(
        private fb: FormBuilder,
        private mealPlanService: MealPlanService,
        private recipeService: RecipeService,
        private router: Router,
        private snackBar: MatSnackBar
    ) {
        this.mealPlanForm = this.fb.group({
            date: [new Date(), [Validators.required]],
            mealType: ['', [Validators.required]],
            recipeId: [''],
            notes: ['']
        });
    }

    ngOnInit(): void {
        this.loadRecipes();
    }

    loadRecipes(): void {
        this.recipeService.getRecipes().subscribe({
            next: (recipes) => {
                this.recipes = recipes;
            },
            error: (error) => {
                console.error('Error loading recipes:', error);
                this.snackBar.open('Failed to load recipes', 'Close', { duration: 3000 });
            }
        });
    }

    onSubmit(): void {
        if (this.mealPlanForm.invalid) {
            this.snackBar.open('Please fill in all required fields', 'Close', { duration: 3000 });
            return;
        }

        this.isSubmitting = true;
        const formValue = this.mealPlanForm.value;

        const request: MealPlanCreateRequestModel = {
            date: formValue.date,
            mealTypeId: formValue.mealTypeId,
            recipeId: formValue.recipeId || undefined,
            notes: formValue.notes || undefined
        };

        this.mealPlanService.createMealPlan(request).subscribe({
            next: (response) => {
                this.snackBar.open('Meal plan created successfully!', 'Close', { duration: 3000 });
                this.router.navigate(['/meal-plan']);
            },
            error: (error) => {
                console.error('Error creating meal plan:', error);
                this.snackBar.open('Failed to create meal plan. Please try again.', 'Close', { duration: 3000 });
                this.isSubmitting = false;
            }
        });
    }

    onCancel(): void {
        this.router.navigate(['/meal-plan']);
    }
} 