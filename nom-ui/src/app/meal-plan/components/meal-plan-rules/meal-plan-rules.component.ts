import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
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
import { MealPlanRuleModel, MealPlanRuleCreateRequestModel, MealPlanRuleResponseModel } from '../../models/meal-plan-rule.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-meal-plan-rules',
  standalone: true,
  imports: [
    CommonModule,
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
    MatMenuModule,
  ],
  templateUrl: './meal-plan-rules.component.html',
  styleUrls: ['./meal-plan-rules.component.scss']
})
export class MealPlanRulesComponent implements OnInit {
  rules: MealPlanRuleResponseModel[] = [];
  isLoading = false;
  error: string | null = null;
  ruleForm: FormGroup;
  isAddingRule = false;

  mealTypes = [
    { id: 1, name: 'Breakfast' },
    { id: 2, name: 'Lunch' },
    { id: 3, name: 'Dinner' },
    { id: 4, name: 'Snack' }
  ];

  daysOfWeek = [
    { id: 1, name: 'Monday' },
    { id: 2, name: 'Tuesday' },
    { id: 3, name: 'Wednesday' },
    { id: 4, name: 'Thursday' },
    { id: 5, name: 'Friday' },
    { id: 6, name: 'Saturday' },
    { id: 7, name: 'Sunday' }
  ];

  constructor(
    private mealPlanService: MealPlanService,
    private router: Router,
    private fb: FormBuilder,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {
    this.ruleForm = this.fb.group({
      dayOfWeekId: ['', [Validators.required]],
      mealTypeId: ['', [Validators.required]],
      queryFilterString: ['']
    });
  }

  ngOnInit(): void {
    this.loadRules();
  }

  loadRules(): void {
    this.isLoading = true;
    this.error = null;

    // TODO: Implement get rules service method when API is available
    // For now, using mock data
    setTimeout(() => {
      this.rules = [];
      this.isLoading = false;
    }, 1000);
  }

  onSubmit(): void {
    if (this.ruleForm.invalid) {
      this.snackBar.open('Please fill in all required fields', 'Close', { duration: 3000 });
      return;
    }

    this.isAddingRule = true;
    const formValue = this.ruleForm.value;

    const request: MealPlanRuleCreateRequestModel = {
      householdId: 1, // TODO: Get from user context
      dayOfWeekId: formValue.dayOfWeekId,
      mealTypeId: formValue.mealTypeId,
      queryFilterString: formValue.queryFilterString || undefined
    };

    this.mealPlanService.createRule(request).subscribe({
      next: (response) => {
        this.snackBar.open('Rule created successfully!', 'Close', { duration: 3000 });
        this.ruleForm.reset();
        this.isAddingRule = false;
        this.loadRules();
      },
      error: (error) => {
        console.error('Error creating rule:', error);
        this.snackBar.open('Failed to create rule. Please try again.', 'Close', { duration: 3000 });
        this.isAddingRule = false;
      }
    });
  }

  onDeleteRule(ruleId: number): void {
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
        this.mealPlanService.deleteRule(ruleId).subscribe({
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
    const mealType = this.mealTypes.find(type => type.id === mealTypeId);
    return mealType?.name || 'Unknown';
  }

  getDayOfWeekName(dayOfWeekId: number): string {
    const day = this.daysOfWeek.find(d => d.id === dayOfWeekId);
    return day?.name || 'Unknown';
  }

  onBack(): void {
    this.router.navigate(['/meal-plan']);
  }
} 