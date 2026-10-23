import { Injectable, inject } from '@angular/core';
import { ComponentType } from '@angular/cdk/portal';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { HouseholdShoppingService } from '../../core/services/household-shopping.service';
import { VerificationFlowResult } from '../../core/models/household-shopping.model';
import { VerificationSheet, VerificationSheetData } from './verification-sheet.component';
import { LabelConfirmResult, LabelConfirmSheet } from './label-confirm-sheet.component';
import { MeasurementsResult, MeasurementsSheet } from './measurements-sheet.component';

export const SHEET_PANEL_CLASS = 'nom-sheet-panel';

/**
 * Walks a household member through the sheets after they tick a food without
 * verified nutrition: scan or skip, confirm what the label said, then optional
 * measurements. Closing the first sheet any other way counts as a skip, so the
 * same food is not asked about again mid-trip. The check-off itself never
 * waits on this.
 */
@Injectable()
export class VerificationFlowService {
  private bottomSheet = inject(MatBottomSheet);
  private shopping = inject(HouseholdShoppingService);
  private snackBar = inject(MatSnackBar);

  async run(item: { ingredientId: number; name: string }, householdId: number): Promise<void> {
    const data: VerificationSheetData = { ingredientId: item.ingredientId, name: item.name, householdId };
    try {
      for (;;) {
        const choice = await this.open<VerificationFlowResult | undefined>(VerificationSheet, data);
        if (!choice || choice.action === 'skip') {
          await firstValueFrom(this.shopping.skipVerification(householdId, item.ingredientId));
          return;
        }
        if (choice.action === 'barcode') {
          await this.open<MeasurementsResult>(MeasurementsSheet, data);
          return;
        }
        const confirmed = await this.open<LabelConfirmResult>(LabelConfirmSheet, { ...data, scan: choice.scan });
        if (confirmed && 'retake' in confirmed) continue;
        if (confirmed && 'saved' in confirmed) await this.open<MeasurementsResult>(MeasurementsSheet, data);
        return;
      }
    } catch {
      this.snackBar.open('Could not save that — the item is still checked off', 'OK', { duration: 4000 });
    }
  }

  private open<T>(component: ComponentType<unknown>, data: unknown): Promise<T | undefined> {
    const ref = this.bottomSheet.open(component, { data, panelClass: SHEET_PANEL_CLASS });
    return firstValueFrom(ref.afterDismissed()) as Promise<T | undefined>;
  }
}
