import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwButtonComponent, AmwDatepickerComponent, AmwCardComponent } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { MealPlanUpdateRequestModel } from '../../models/meal-plan-update-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'nom-meal-plan-edit',
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
  templateUrl: './meal-plan-edit.component.html',
  styleUrls: ['./meal-plan-edit.component.scss']
})
export class MealPlanEditComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private notificationService = inject(NotificationService);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]]
  });

  isLoading = signal(false);
  mealPlanId = signal(0);
  mealPlan = signal<MealPlanResponseModel | null>(null);

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.mealPlanId.set(+params['id']);
      this.loadMealPlan();
    });
  }

  loadMealPlan(): void {
    this.isLoading.set(true);

    this.mealPlanService.getMealPlan(this.mealPlanId()).subscribe({
      next: (mealPlan) => {
        this.mealPlan.set(mealPlan);
        this.mealPlanForm.patchValue({
          recipeName: mealPlan.recipeName,
          mealType: mealPlan.mealType,
          date: new Date(mealPlan.date),
          description: mealPlan.description || ''
        });
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading meal plan:', error);
        this.notificationService.error('Failed to load meal plan details');
        this.router.navigate(['/meal-plan']);
      }
    });
  }

  onSubmit(): void {
    if (this.mealPlanForm.valid && this.mealPlan()) {
      this.isLoading.set(true);

      const updateRequest = new MealPlanUpdateRequestModel({
        recipeName: this.mealPlanForm.value.recipeName,
        mealTypeId: this.getMealTypeId(this.mealPlanForm.value.mealType),
        date: this.mealPlanForm.value.date,
        description: this.mealPlanForm.value.description
      });

      this.mealPlanService.updateMealPlan(this.mealPlanId(), updateRequest).subscribe({
        next: () => {
          this.isLoading.set(false);
          this.notificationService.success('Meal plan updated successfully!');
          this.router.navigate(['/meal-plan', this.mealPlanId()]);
        },
        error: (error) => {
          this.isLoading.set(false);
          console.error('Error updating meal plan:', error);
          this.notificationService.error('Failed to update meal plan. Please try again.');
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/meal-plan', this.mealPlanId()]);
  }

  private getMealTypeId(mealType: string): number {
    switch (mealType) {
      case 'breakfast': return 1;
      case 'lunch': return 2;
      case 'dinner': return 3;
      case 'snack': return 4;
      default: return 3; // Default to dinner
    }
  }
} 