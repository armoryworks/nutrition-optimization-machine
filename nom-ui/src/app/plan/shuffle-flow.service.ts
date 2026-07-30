import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { EMPTY, Observable, defer, switchMap } from 'rxjs';
import { MealPlanService } from '../core/services/meal-plan.service';
import { MealPlanDay } from '../core/models/meal-plan-day.model';
import { MealPlanShuffleResponse } from '../core/models/meal-plan-shuffle-response.model';
import { ShuffleConfirmDialog, ShuffleConfirmResult } from './shuffle-confirm-dialog.component';

export interface ShuffleFlowOptions {
  householdId: number;
  /** Days considered when deciding whether confirmation is needed. */
  days: MealPlanDay[];
  startDate: string;
  endDate: string;
  /** Invoked when the shuffle request actually starts (after any confirmation). */
  onShuffleStart?: () => void;
}

/**
 * Shared shuffle flow (used by the plan calendar and the dashboard's
 * "shuffle today"): if any slot in range still holds unshopped meals, ask
 * whether to fill only empty slots or replace them; otherwise shuffle empty
 * slots directly. Completes without emitting when cancelled or nothing to do.
 */
@Injectable({ providedIn: 'root' })
export class ShuffleFlowService {
  private dialog = inject(MatDialog);
  private mealPlanService = inject(MealPlanService);

  run(options: ShuffleFlowOptions): Observable<MealPlanShuffleResponse> {
    const unshoppedEntries = (c: { entries: { shoppingCompletedAt: string | null }[] }) =>
      c.entries.filter((e) => !e.shoppingCompletedAt);
    const hasFilledSlots = options.days.some((d) =>
      d.cells.some((c) => unshoppedEntries(c).length > 0),
    );
    const hasEmptySlots = options.days.some((d) => d.cells.some((c) => c.entries.length === 0));

    const doShuffle = (replaceExisting: boolean) =>
      defer(() => {
        options.onShuffleStart?.();
        return this.mealPlanService.shuffle({
          householdId: options.householdId,
          startDate: options.startDate,
          endDate: options.endDate,
          replaceExisting,
        });
      });

    if (hasFilledSlots) {
      const dialogRef = this.dialog.open(ShuffleConfirmDialog, { width: '400px' });
      return dialogRef.afterClosed().pipe(
        switchMap((result: ShuffleConfirmResult) => {
          if (result === 'empty') return doShuffle(false);
          if (result === 'replace') return doShuffle(true);
          return EMPTY;
        }),
      );
    }
    if (hasEmptySlots) {
      return doShuffle(false);
    }
    return EMPTY;
  }
}
