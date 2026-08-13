import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { HouseholdStore } from '../core/services/household-store';
import { LoadingService } from '../core/services/loading.service';
import { MealPlanRule } from '../core/models/meal-plan-rule.model';
import { NoHouseholdCta } from '../shared/components/no-household-cta/no-household-cta.component';

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
    NoHouseholdCta,
  ],
  templateUrl: './plan-rules.component.html',
  styleUrl: './plan-rules.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanRules implements OnInit {
  private fb = inject(FormBuilder);
  private mealPlanService = inject(MealPlanService);
  private householdStore = inject(HouseholdStore);
  private loadingService = inject(LoadingService);

  private destroyRef = inject(DestroyRef);

  rules = signal<MealPlanRule[]>([]);
  loading = signal(true);
  /** Household-absence is a setup state, not an error — it gets the shared CTA. */
  noHousehold = signal(false);
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
    this.loadHouseholdThenRules();
  }

  /** The no-household CTA created a kitchen — reload page state in place. */
  onHouseholdCreated(): void {
    this.loading.set(true);
    this.loadHouseholdThenRules();
  }

  private loadHouseholdThenRules(): void {
    this.householdStore.getHouseholds().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.noHousehold.set(false);
          this.householdId.set(list[0].id);
          this.loadRules();
        } else {
          this.loading.set(false);
          this.noHousehold.set(true);
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
      this.loadingService.loading('Loading rules...'),
      takeUntilDestroyed(this.destroyRef),
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
    if (this.ruleForm.invalid) {
      this.ruleForm.markAllAsTouched();
      return;
    }
    if (this.saving()) return;
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
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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
    this.mealPlanService.deleteRule(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => this.rules.update(r => r.filter(rule => rule.id !== id)),
      error: () => this.errorMessage.set('Failed to delete rule.'),
    });
  }
}
