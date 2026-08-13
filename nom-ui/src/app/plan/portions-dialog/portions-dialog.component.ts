import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PortionService } from '../../core/services/portion.service';
import { PortionBreakdown } from '../../core/models/portion.model';

export interface PortionsDialogData {
  householdId: number;
  date: string;
  mealTypeId: number;
  mealType: string;
}

/**
 * Per-member portion breakdown for one planned meal: how much to cook
 * (cook factor per recipe) and how to split it (plates per member), from
 * macro-goal calorie targets and the household meal split.
 */
@Component({
  selector: 'nom-portions-dialog',
  imports: [DecimalPipe, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './portions-dialog.component.html',
  styleUrl: './portions-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortionsDialog implements OnInit {
  readonly data = inject<PortionsDialogData>(MAT_DIALOG_DATA);
  private portionService = inject(PortionService);
  private destroyRef = inject(DestroyRef);

  loading = signal(true);
  breakdown = signal<PortionBreakdown | null>(null);
  errorMessage = signal('');

  ngOnInit(): void {
    this.portionService
      .getPortions(this.data.householdId, this.data.date, this.data.mealTypeId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (b) => {
          this.breakdown.set(b);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.errorMessage.set(
            err.status === 404
              ? 'No planned recipes in this meal slot yet.'
              : 'Unable to compute portions. Please try again.',
          );
        },
      });
  }
}
