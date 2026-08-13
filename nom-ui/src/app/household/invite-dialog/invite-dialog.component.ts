import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { HouseholdService } from '../../core/services/household.service';

export interface InviteDialogData {
  householdId: number;
}

/**
 * Generates a household invite code and presents it for sharing — the other
 * person redeems it via "Join Household".
 */
@Component({
  selector: 'nom-invite-dialog',
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
  ],
  templateUrl: './invite-dialog.component.html',
  styleUrl: './invite-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InviteDialog implements OnInit {
  data: InviteDialogData = inject(MAT_DIALOG_DATA);
  private householdService = inject(HouseholdService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  token = signal('');
  loading = signal(true);
  errorMessage = signal('');

  ngOnInit(): void {
    this.householdService.createInviteToken({
      householdId: this.data.householdId,
      name: null,
      usesLeft: null,
      expirationDate: null,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (response) => {
        this.token.set(response.token);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  copyToken(): void {
    navigator.clipboard.writeText(this.token()).then(() => {
      this.snackBar.open('Invite code copied', 'OK', { duration: 2000 });
    });
  }

  private friendlyError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.error?.reason === 'personal_household') {
      return 'This is a personal kitchen — convert it into a shared household first.';
    }
    return 'Unable to create an invite code. Please try again.';
  }
}
