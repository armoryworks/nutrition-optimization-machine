import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { forkJoin, switchMap } from 'rxjs';
import { PortionService } from '../../../core/services/portion.service';
import { MealSplit } from '../../../core/models/portion.model';

/**
 * Editor for the household's meal-split percentages (how the daily calorie
 * budget divides across Breakfast/Lunch/Dinner/Snacks). Must sum to 100.
 * Saves require household manage permission — the API's 403 surfaces inline.
 */
@Component({
  selector: 'nom-meal-split-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatSlideToggleModule],
  templateUrl: './meal-split-form.component.html',
  styleUrl: './meal-split-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MealSplitForm implements OnInit {
  householdId = input.required<number>();

  private portionService = inject(PortionService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  splitForm = this.fb.group({
    breakfastPct: [25, [Validators.required, Validators.min(0), Validators.max(100)]],
    lunchPct: [30, [Validators.required, Validators.min(0), Validators.max(100)]],
    dinnerPct: [35, [Validators.required, Validators.min(0), Validators.max(100)]],
    snacksPct: [10, [Validators.required, Validators.min(0), Validators.max(100)]],
    dessertPct: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  private formValue = toSignal(this.splitForm.valueChanges, { initialValue: this.splitForm.value });

  total = computed(() => {
    const v = this.formValue();
    return (v.breakfastPct ?? 0) + (v.lunchPct ?? 0) + (v.dinnerPct ?? 0) + (v.snacksPct ?? 0) + (v.dessertPct ?? 0);
  });

  includeDessert = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  readonly fields = [
    { control: 'breakfastPct', label: 'Breakfast', icon: 'free_breakfast', testid: 'meal-split-breakfast' },
    { control: 'lunchPct', label: 'Lunch', icon: 'lunch_dining', testid: 'meal-split-lunch' },
    { control: 'dinnerPct', label: 'Dinner', icon: 'dinner_dining', testid: 'meal-split-dinner' },
    { control: 'snacksPct', label: 'Snacks', icon: 'eco', testid: 'meal-split-snacks' },
  ] as const;

  readonly dessertField = { control: 'dessertPct', label: 'Dessert', icon: 'cake', testid: 'meal-split-dessert' } as const;

  ngOnInit(): void {
    forkJoin({
      split: this.portionService.getMealSplit(this.householdId()),
      options: this.portionService.getMealPlanOptions(this.householdId()),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ split, options }) => {
          this.splitForm.patchValue(split);
          this.includeDessert.set(options.includeDessert);
        },
        error: () => this.errorMessage.set('Unable to load meal split.'),
      });
  }

  onDessertToggle(include: boolean): void {
    this.includeDessert.set(include);
    this.successMessage.set('');
    const v = this.splitForm.getRawValue();
    if (include && !v.dessertPct) {
      const share = Math.min(5, v.snacksPct ?? 0);
      this.splitForm.patchValue({ dessertPct: share, snacksPct: (v.snacksPct ?? 0) - share });
    } else if (!include && v.dessertPct) {
      this.splitForm.patchValue({ snacksPct: (v.snacksPct ?? 0) + (v.dessertPct ?? 0), dessertPct: 0 });
    }
  }

  onSave(): void {
    if (this.splitForm.invalid || this.total() !== 100) {
      this.splitForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    this.portionService
      .saveMealPlanOptions(this.householdId(), { includeDessert: this.includeDessert() })
      .pipe(
        switchMap(() => this.portionService.saveMealSplit(this.householdId(), this.splitForm.getRawValue() as MealSplit)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.successMessage.set('Meal split saved.');
        },
        error: (err) => {
          this.saving.set(false);
          this.errorMessage.set(
            err.status === 403
              ? "You don't have permission to change the meal split."
              : 'Unable to save meal split. Please try again.',
          );
        },
      });
  }
}
