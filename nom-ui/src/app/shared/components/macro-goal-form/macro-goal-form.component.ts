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
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MacroGoalService } from '../../../core/services/macro-goal.service';
import { MacroGoal } from '../../../core/models/macro-goal.model';

/**
 * Self-contained editor for daily macro targets. Give it exactly one of
 * personId / householdId and it loads and saves the corresponding goal.
 * Leave a field blank for "no target". Household saves require manage
 * permission — the API's 403 is surfaced inline.
 */
@Component({
  selector: 'nom-macro-goal-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
  templateUrl: './macro-goal-form.component.html',
  styleUrl: './macro-goal-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MacroGoalForm implements OnInit {
  personId = input<number | null>(null);
  householdId = input<number | null>(null);

  private macroGoalService = inject(MacroGoalService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  goalForm = this.fb.group({
    caloriesTarget: [null as number | null, [Validators.min(0), Validators.max(20000)]],
    proteinGramsTarget: [null as number | null, [Validators.min(0), Validators.max(2000)]],
    carbGramsTarget: [null as number | null, [Validators.min(0), Validators.max(3000)]],
    fatGramsTarget: [null as number | null, [Validators.min(0), Validators.max(1500)]],
  });

  loading = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  readonly fields = [
    { control: 'caloriesTarget', label: 'Calories', unit: 'kcal', icon: 'local_fire_department', testid: 'macro-goal-calories' },
    { control: 'proteinGramsTarget', label: 'Protein', unit: 'g', icon: 'fitness_center', testid: 'macro-goal-protein' },
    { control: 'carbGramsTarget', label: 'Carbs', unit: 'g', icon: 'grain', testid: 'macro-goal-carbs' },
    { control: 'fatGramsTarget', label: 'Fat', unit: 'g', icon: 'water_drop', testid: 'macro-goal-fat' },
  ] as const;

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    const personId = this.personId();
    const householdId = this.householdId();
    if (!personId && !householdId) return;

    this.loading.set(true);
    const source$ = personId
      ? this.macroGoalService.getPersonGoal(personId)
      : this.macroGoalService.getHouseholdGoal(householdId!);

    source$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (goal) => {
        this.goalForm.patchValue(goal);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to load macro goals.');
      },
    });
  }

  onSave(): void {
    if (this.goalForm.invalid) {
      this.goalForm.markAllAsTouched();
      return;
    }

    const personId = this.personId();
    const householdId = this.householdId();
    if (!personId && !householdId) return;

    const goal = this.goalForm.getRawValue() as MacroGoal;
    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const save$ = personId
      ? this.macroGoalService.savePersonGoal(personId, goal)
      : this.macroGoalService.saveHouseholdGoal(householdId!, goal);

    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => {
        this.goalForm.patchValue(saved);
        this.saving.set(false);
        this.successMessage.set('Macro goals saved.');
      },
      error: (err) => {
        this.saving.set(false);
        this.errorMessage.set(
          err.status === 403
            ? "You don't have permission to change these goals."
            : 'Unable to save macro goals. Please try again.',
        );
      },
    });
  }

  onClear(): void {
    this.goalForm.reset();
    this.successMessage.set('');
    this.errorMessage.set('');
  }
}
