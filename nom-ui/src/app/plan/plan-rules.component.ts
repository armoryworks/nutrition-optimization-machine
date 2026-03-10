import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MealPlanService } from '../core/services/meal-plan.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { MealPlanRule } from '../core/models/meal-plan-rule.model';

@Component({
  selector: 'nom-plan-rules',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './plan-rules.component.html',
  styleUrl: './plan-rules.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanRules implements OnInit {
  private fb = inject(FormBuilder);
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);

  rules = signal<MealPlanRule[]>([]);
  loading = signal(true);
  saving = signal(false);
  errorMessage = signal('');
  householdId = signal(0);

  ruleForm = this.fb.group({
    queryFilter: ['', [Validators.maxLength(2047)]],
    mealTypeId: [null as number | null],
    dayOfWeekId: [null as number | null],
    maxRecipes: [3, [Validators.required, Validators.min(1), Validators.max(10)]],
    isActive: [true],
  });

  ngOnInit(): void {
    this.householdService.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.householdId.set(list[0].id);
          this.loadRules();
        } else {
          this.loading.set(false);
          this.errorMessage.set('Create a household first to manage rules.');
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load household.');
      },
    });
  }

  private loadRules(): void {
    this.loading.set(true);
    this.mealPlanService.getRules(this.householdId()).pipe(
      this.loadingService.loading('Loading rules...')
    ).subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load rules.');
      },
    });
  }

  onAddRule(): void {
    if (this.ruleForm.invalid || this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const form = this.ruleForm.getRawValue();
    this.mealPlanService.createRule({
      householdId: this.householdId(),
      mealTypeId: form.mealTypeId,
      dayOfWeekId: form.dayOfWeekId,
      queryFilter: form.queryFilter ?? '',
      maxRecipes: form.maxRecipes!,
      isActive: form.isActive ?? true,
    }).subscribe({
      next: (rule) => {
        this.rules.update(r => [...r, rule]);
        this.ruleForm.reset({ maxRecipes: 3, isActive: true });
        this.saving.set(false);
      },
      error: () => {
        this.saving.set(false);
        this.errorMessage.set('Failed to create rule.');
      },
    });
  }

  deleteRule(id: number): void {
    this.mealPlanService.deleteRule(id).subscribe({
      next: () => this.rules.update(r => r.filter(rule => rule.id !== id)),
      error: () => this.errorMessage.set('Failed to delete rule.'),
    });
  }
}
