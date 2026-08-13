import { Component, inject, signal, ChangeDetectionStrategy, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { HttpErrorResponse } from '@angular/common/http';
import { HouseholdStore } from '../../core/services/household-store';

export interface ConvertHouseholdDialogData {
  householdId: number;
}

/** True when the personal kitchen was converted (the caller continues into the invite flow). */
export type ConvertHouseholdDialogResult = boolean | undefined;

/**
 * The first-invite interstitial for a personal kitchen: inviting someone
 * turns it into a shared household. Confirming renames the household and
 * clears the personal flag; the caller then proceeds into the invite flow.
 */
@Component({
  selector: 'nom-convert-household-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './convert-household-dialog.component.html',
  styleUrl: './convert-household-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConvertHouseholdDialog {
  private dialogRef = inject(MatDialogRef<ConvertHouseholdDialog>);
  data: ConvertHouseholdDialogData = inject(MAT_DIALOG_DATA);
  private householdStore = inject(HouseholdStore);
  private destroyRef = inject(DestroyRef);

  name = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(255)] });
  converting = signal(false);
  errorMessage = signal('');

  convertAndContinue(): void {
    if (this.name.invalid) {
      this.name.markAsTouched();
      return;
    }
    if (this.converting()) return;

    this.converting.set(true);
    this.errorMessage.set('');

    this.householdStore.convertToShared(this.data.householdId, this.name.value.trim()).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.converting.set(false);
        this.dialogRef.close(true as ConvertHouseholdDialogResult);
      },
      error: (err) => {
        this.converting.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }

  private friendlyError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.error?.reason === 'steward_required') {
        return 'Only the household steward can do this.';
      }
      if (err.error?.reason === 'not_personal') {
        return 'This household is already shared — you can invite someone directly.';
      }
    }
    return 'Unable to create the household. Please try again.';
  }
}
