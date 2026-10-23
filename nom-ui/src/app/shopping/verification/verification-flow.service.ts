import { Injectable, inject } from '@angular/core';
import { ComponentType } from '@angular/cdk/portal';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { HouseholdShoppingService } from '../../core/services/household-shopping.service';
import { VerificationFlowOutcome, VerificationFlowResult } from '../../core/models/household-shopping.model';
import { VerificationSheet, VerificationSheetData } from './verification-sheet.component';
import { LabelConfirmResult, LabelConfirmSheet } from './label-confirm-sheet.component';
import { MeasurementsResult, MeasurementsSheet } from './measurements-sheet.component';

export const SHEET_PANEL_CLASS = 'nom-sheet-panel';
export const SKIPPED_MESSAGE = 'Skipped — NOM will ask about this food again in a month';

export interface VerificationFlowItem {
  ingredientId: number;
  name: string;
  adhoc?: boolean;
}

/**
 * Walks a household member through the sheets after they tick a food without
 * verified nutrition: scan, skip or say it is not a food, confirm what the label
 * said, then optional measurements. Only the Skip button records a skip; a tap
 * outside the sheet or the X leaves nothing behind, and the same food is not
 * asked about again mid-trip either way. The check-off itself never waits on this.
 */
@Injectable()
export class VerificationFlowService {
  private bottomSheet = inject(MatBottomSheet);
  private shopping = inject(HouseholdShoppingService);
  private snackBar = inject(MatSnackBar);

  async run(item: VerificationFlowItem, householdId: number): Promise<VerificationFlowOutcome> {
    const data: VerificationSheetData = { ingredientId: item.ingredientId, name: item.name, householdId, adhoc: item.adhoc === true };
    try {
      for (;;) {
        const choice = await this.open<VerificationFlowResult | undefined>(VerificationSheet, data);
        if (!choice) return 'dismissed';
        if (choice.action === 'skip') {
          await firstValueFrom(this.shopping.skipVerification(householdId, item.ingredientId));
          this.snackBar.open(SKIPPED_MESSAGE, 'OK', { duration: 4000 });
          return 'skipped';
        }
        if (choice.action === 'not-food') {
          this.snackBar.open(`${item.name} is kept on your list as a non-food item`, 'OK', { duration: 4000 });
          return 'not-food';
        }
        if (choice.action === 'barcode') {
          await this.open<MeasurementsResult>(MeasurementsSheet, data);
          return 'recorded';
        }
        const confirmed = await this.open<LabelConfirmResult>(LabelConfirmSheet, { ...data, scan: choice.scan });
        if (confirmed && 'retake' in confirmed) continue;
        if (confirmed && 'saved' in confirmed) {
          await this.open<MeasurementsResult>(MeasurementsSheet, data);
          return 'recorded';
        }
        return 'dismissed';
      }
    } catch {
      this.snackBar.open('Could not save that — the item is still checked off', 'OK', { duration: 4000 });
      return 'dismissed';
    }
  }

  private open<T>(component: ComponentType<unknown>, data: unknown): Promise<T | undefined> {
    const ref = this.bottomSheet.open(component, { data, panelClass: SHEET_PANEL_CLASS });
    return firstValueFrom(ref.afterDismissed()) as Promise<T | undefined>;
  }
}
