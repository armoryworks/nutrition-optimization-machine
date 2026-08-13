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
import { PortionService } from '../../../core/services/portion.service';
import { MealSplit } from '../../../core/models/portion.model';

/**
 * Editor for the household's meal-split percentages (how the daily calorie
 * budget divides across Breakfast/Lunch/Dinner/Snacks). Must sum to 100.
 * Saves require household manage permission — the API's 403 surfaces inline.
 */
@Component({
  selector: 'nom-meal-split-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
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
  });

  private formValue = toSignal(this.splitForm.valueChanges, { initialValue: this.splitForm.value });

  total = computed(() => {
    const v = this.formValue();
    return (v.breakfastPct ?? 0) + (v.lunchPct ?? 0) + (v.dinnerPct ?? 0) + (v.snacksPct ?? 0);
  });

  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  readonly fields = [
    { control: 'breakfastPct', label: 'Breakfast', icon: 'free_breakfast', testid: 'meal-split-breakfast' },
    { control: 'lunchPct', label: 'Lunch', icon: 'lunch_dining', testid: 'meal-split-lunch' },
    { control: 'dinnerPct', label: 'Dinner', icon: 'dinner_dining', testid: 'meal-split-dinner' },
    { control: 'snacksPct', label: 'Snacks', icon: 'cookie', testid: 'meal-split-snacks' },
  ] as const;

  ngOnInit(): void {
    this.portionService
      .getMealSplit(this.householdId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (split) => this.splitForm.patchValue(split),
        error: () => this.errorMessage.set('Unable to load meal split.'),
      });
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
      .saveMealSplit(this.householdId(), this.splitForm.getRawValue() as MealSplit)
      .pipe(takeUntilDestroyed(this.destroyRef))
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
