import { Component, OnInit, inject, signal } from '@angular/core';

import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanRuleCreateRequestModel } from '../../models/meal-plan-rule-create-request.model';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { MealPlanRuleResponseModel } from '../../models/meal-plan-rule-response.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'nom-meal-plan-rules',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatSelectModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule
],
  templateUrl: './meal-plan-rules.component.html',
  styleUrls: ['./meal-plan-rules.component.scss']
})
export class MealPlanRulesComponent implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private referenceDataService = inject(ReferenceDataService);
  private router = inject(Router);
  private nonNullableFb = inject(NonNullableFormBuilder);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  rules = signal<MealPlanRuleResponseModel[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  ruleForm: FormGroup;
  isAddingRule = signal(false);

  mealTypes = signal<ReferenceItemModel[]>([]);
  daysOfWeek = signal<ReferenceItemModel[]>([]);



  constructor() {
    this.ruleForm = this.nonNullableFb.group({
      dayOfWeekId: ['', [Validators.required]],
      mealTypeId: ['', [Validators.required]],
      queryFilterString: ['']
    });
  }

  ngOnInit(): void {
    this.loadRules();
    this.loadReferenceData();
  }

  private loadReferenceData(): void {
    // Load meal types from backend
    this.referenceDataService.getMealTypes().subscribe({
      next: (mealTypes) => {
        this.mealTypes.set(mealTypes);
      },
      error: (error) => {
        console.error('Error loading meal types:', error);
        this.mealTypes.set([]);
      }
    });

    // Load days of week
    this.referenceDataService.getDaysOfWeek().subscribe({
      next: (days) => {
        this.daysOfWeek.set(days);
      },
      error: (error) => {
        console.error('Error loading days of week:', error);
        this.daysOfWeek.set([]);
      }
    });
  }

  loadRules(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService.getRules().subscribe({
      next: (rules: MealPlanRuleResponseModel[]) => {
        this.rules.set(rules);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        console.error('Error loading rules:', error);
        this.error.set('Failed to load meal plan rules');
        this.isLoading.set(false);
        this.snackBar.open('Failed to load meal plan rules', 'Close', { duration: 3000 });
      }
    });
  }

  onSubmit(): void {
    if (this.ruleForm.invalid) {
      this.snackBar.open('Please fill in all required fields', 'Close', { duration: 3000 });
      return;
    }

    this.isAddingRule.set(true);
    const formValue = this.ruleForm.value;

    const request: MealPlanRuleCreateRequestModel = {
      id: 0,
      householdId: this.getCurrentHouseholdId(),
      name: formValue.name || 'Meal Plan Rule',
      dayOfWeekId: formValue.dayOfWeekId,
      mealTypeId: formValue.mealTypeId,
      queryFilterString: formValue.queryFilterString || undefined,
      isActive: true
    };

    this.mealPlanService.createRule(request).subscribe({
      next: () => {
        this.snackBar.open('Rule created successfully!', 'Close', { duration: 3000 });
        this.ruleForm.reset();
        this.isAddingRule.set(false);
        this.loadRules();
      },
      error: (error) => {
        console.error('Error creating rule:', error);
        this.snackBar.open('Failed to create rule. Please try again.', 'Close', { duration: 3000 });
        this.isAddingRule.set(false);
      }
    });
  }

  editRule(rule: MealPlanRuleResponseModel): void {
    // Navigate to edit rule page
    this.router.navigate(['/meal-plan/rules', rule.id, 'edit']);
  }

  deleteRule(rule: MealPlanRuleResponseModel): void {
    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Rule',
        message: 'Are you sure you want to delete this meal plan rule?',
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.mealPlanService.deleteRule(rule.id).subscribe({
          next: () => {
            this.snackBar.open('Rule deleted successfully', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
            this.loadRules();
          },
          error: (error) => {
            console.error('Error deleting rule:', error);
            this.snackBar.open('Failed to delete rule', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
          }
        });
      }
    });
  }

  getMealTypeName(mealTypeId: number): string {
    const mealType = this.mealTypes().find(type => type.id === mealTypeId);
    return mealType?.name || 'Unknown';
  }

  getDayOfWeekName(dayOfWeekId: number): string {
    const day = this.daysOfWeek().find(d => d.id === dayOfWeekId);
    return day?.name || 'Unknown';
  }

  onBack(): void {
    this.router.navigate(['/meal-plan']);
  }

  private getCurrentHouseholdId(): number {
    // TODO: Get from user context service
    // For now, return a default value
    return 1;
  }

  private getCurrentUserId(): number {
    // TODO: Get from user context service
    // For now, return a default value
    return 1;
  }
} 