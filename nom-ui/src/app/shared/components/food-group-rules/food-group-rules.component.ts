import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MealPlanService } from '../../../core/services/meal-plan.service';
import { FoodGroup, FoodGroupRule } from '../../../core/models/food-group.model';

/**
 * Household food-group requirements editor: "at least N servings of {food group}
 * per day or per meal (optionally at one meal)". Meal-plan shuffle guarantees
 * these by counting servings from recipes + standalone foods and topping up with
 * whole foods. Saves require household manage permission (API 403 surfaced inline).
 */
@Component({
  selector: 'nom-food-group-rules',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatButtonToggleModule,
  ],
  templateUrl: './food-group-rules.component.html',
  styleUrl: './food-group-rules.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FoodGroupRules implements OnInit {
  householdId = input.required<number>();

  private mealPlanService = inject(MealPlanService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  // Reference meal types (matches backend seed ids). Null = applies to all meals.
  readonly mealTypes = [
    { id: 1100, name: 'Breakfast' },
    { id: 1101, name: 'Lunch' },
    { id: 1102, name: 'Dinner' },
    { id: 1103, name: 'Snacks' },
  ] as const;

  foodGroups = signal<FoodGroup[]>([]);
  rules = signal<FoodGroupRule[]>([]);
  loading = signal(false);
  saving = signal(false);
  errorMessage = signal('');

  addForm = this.fb.group({
    foodGroupId: [null as number | null, [Validators.required]],
    minServings: [1 as number | null, [Validators.required, Validators.min(0.5), Validators.max(20)]],
    timeframe: ['PerDay' as 'PerDay' | 'PerMeal', [Validators.required]],
    mealTypeId: [null as number | null],
  });

  ngOnInit(): void {
    this.mealPlanService
      .getFoodGroups()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.foodGroups.set(g) });
    this.loadRules();
  }

  private loadRules(): void {
    this.loading.set(true);
    this.mealPlanService
      .getFoodGroupRules(this.householdId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.rules.set(r);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.errorMessage.set('Unable to load food-group requirements.');
        },
      });
  }

  timeframeLabel(rule: FoodGroupRule): string {
    const scope = rule.mealTypeName ? ` (${rule.mealTypeName})` : '';
    return rule.timeframe === 'PerMeal' ? `per meal${scope}` : `per day${scope}`;
  }

  onAdd(): void {
    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }
    const v = this.addForm.getRawValue();
    this.saving.set(true);
    this.errorMessage.set('');
    this.mealPlanService
      .upsertFoodGroupRule({
        householdId: this.householdId(),
        foodGroupId: v.foodGroupId!,
        minServings: v.minServings!,
        timeframe: v.timeframe!,
        mealTypeId: v.mealTypeId ?? null,
        isActive: true,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.addForm.reset({ minServings: 1, timeframe: 'PerDay', mealTypeId: null, foodGroupId: null });
          this.loadRules();
        },
        error: (err) => {
          this.saving.set(false);
          this.errorMessage.set(
            err.status === 403
              ? "You don't have permission to change these requirements."
              : 'Unable to save the requirement. Please try again.',
          );
        },
      });
  }

  onDelete(rule: FoodGroupRule): void {
    this.mealPlanService
      .deleteFoodGroupRule(rule.id, this.householdId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.rules.set(this.rules().filter((r) => r.id !== rule.id)),
        error: (err) => {
          this.errorMessage.set(
            err.status === 403
              ? "You don't have permission to change these requirements."
              : 'Unable to remove the requirement.',
          );
        },
      });
  }
}
