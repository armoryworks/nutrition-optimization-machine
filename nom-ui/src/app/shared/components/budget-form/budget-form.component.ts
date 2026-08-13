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
import { BudgetService } from '../../../core/services/budget.service';
import { Budget } from '../../../core/models/budget.model';

/**
 * Editor for a grocery-spend budget. Give it exactly one of personId /
 * householdId. Household saves require manage permission (the API's 403 is
 * surfaced inline).
 */
@Component({
  selector: 'nom-budget-form',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './budget-form.component.html',
  styleUrl: './budget-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BudgetForm implements OnInit {
  personId = input<number | null>(null);
  householdId = input<number | null>(null);

  private budgetService = inject(BudgetService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  budgetForm = this.fb.group({
    amount: [0, [Validators.required, Validators.min(0), Validators.max(100000)]],
    period: ['weekly' as 'weekly' | 'monthly', Validators.required],
  });

  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  ngOnInit(): void {
    const personId = this.personId();
    const householdId = this.householdId();
    if (!personId && !householdId) return;

    const source$ = personId
      ? this.budgetService.getPersonBudget(personId)
      : this.budgetService.getHouseholdBudget(householdId!);

    source$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (budget) => this.budgetForm.patchValue({ amount: budget.amount, period: budget.period }),
      error: () => this.errorMessage.set('Unable to load budget.'),
    });
  }

  onSave(): void {
    if (this.budgetForm.invalid) {
      this.budgetForm.markAllAsTouched();
      return;
    }

    const personId = this.personId();
    const householdId = this.householdId();
    if (!personId && !householdId) return;

    const budget: Budget = {
      amount: this.budgetForm.value.amount!,
      period: this.budgetForm.value.period!,
      currency: 'USD',
    };
    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const save$ = personId
      ? this.budgetService.savePersonBudget(personId, budget)
      : this.budgetService.saveHouseholdBudget(householdId!, budget);

    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.successMessage.set('Budget saved.');
      },
      error: (err) => {
        this.saving.set(false);
        this.errorMessage.set(
          err.status === 403
            ? "You don't have permission to change this budget."
            : 'Unable to save budget. Please try again.',
        );
      },
    });
  }
}
