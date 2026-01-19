import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwButtonComponent, AmwDatepickerComponent, AmwCardComponent } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanCreateRequestModel } from '../../models/meal-plan-create-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'nom-meal-plan-create',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatProgressBarModule,
    AmwInputComponent,
    AmwSelectComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwDatepickerComponent,
    AmwCardComponent
  ],
  templateUrl: './meal-plan-create.component.html',
  styleUrls: ['./meal-plan-create.component.scss']
})
export class MealPlanCreateComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private router = inject(Router);
  private notificationService = inject(NotificationService);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = signal(false);

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  ngOnInit(): void {
    // No need to set AuthorId - it will be handled by the backend
  }

  onSubmit(): void {
    if (this.mealPlanForm.valid) {
      this.isLoading.set(true);

      const createRequest = new MealPlanCreateRequestModel({
        recipeName: this.mealPlanForm.value.recipeName,
        mealType: this.mealPlanForm.value.mealType,
        date: this.mealPlanForm.value.date,
        description: this.mealPlanForm.value.description
      });

      this.mealPlanService.createMealPlan(createRequest).subscribe({
        next: (response) => {
          this.isLoading.set(false);
          this.notificationService.success('Meal plan created successfully!');
          this.router.navigate(['/meal-plan', response.id]);
        },
        error: (error) => {
          this.isLoading.set(false);
          console.error('Error creating meal plan:', error);
          this.notificationService.error('Failed to create meal plan. Please try again.');
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/meal-plan']);
  }
} 