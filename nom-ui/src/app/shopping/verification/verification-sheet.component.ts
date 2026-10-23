import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { HouseholdShoppingService } from '../../core/services/household-shopping.service';
import { BarcodeDecoder } from '../../core/utils/barcode-detect';
import { BarcodeResult, VerificationFlowResult } from '../../core/models/household-shopping.model';

export interface VerificationSheetData {
  ingredientId: number;
  name: string;
  householdId: number;
  /** True for a line the household typed in, which may turn out not to be a food at all. */
  adhoc?: boolean;
}

export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
export const PHOTO_TOO_LARGE = 'That photo is over 10 MB — take it again at a lower resolution';
export const UPLOAD_TOO_LARGE = 'That photo is too large to upload — take it again at a lower resolution';
export const NO_BARCODE_FOUND = 'No barcode found — try a sharper photo, or skip';
export const LABEL_UNREADABLE = 'Could not read that photo — try again, or skip';
export const NOT_FOOD_FAILED = 'Could not save that — try again, or skip';

/**
 * The server explains a refusal (hourly cap, file type, size) in `message`; that
 * text goes to the shopper as-is so she does not retake a photo that can never
 * succeed. Anything else keeps the retake wording.
 */
export function uploadErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  const message = typeof error.error?.message === 'string' ? error.error.message.trim() : '';
  if (error.status === 413) return message || UPLOAD_TOO_LARGE;
  if (error.status === 400 || error.status === 429) return message || fallback;
  return fallback;
}

/**
 * First stop after ticking a food NOM knows little about: photograph the
 * Nutrition Facts panel, photograph the barcode, say it is not a food, or skip
 * for a month. The uploads happen here so the flow receives a finished result.
 */
@Component({
  selector: 'nom-verification-sheet',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './verification-sheet.component.html',
  styleUrl: './verification-sheet.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VerificationSheet {
  readonly data = inject<VerificationSheetData>(MAT_BOTTOM_SHEET_DATA);
  private sheetRef = inject(MatBottomSheetRef<VerificationSheet, VerificationFlowResult | undefined>);
  private shopping = inject(HouseholdShoppingService);
  private decoder = inject(BarcodeDecoder);
  private destroyRef = inject(DestroyRef);

  busy = signal<'' | 'label' | 'barcode' | 'not-food'>('');
  error = signal('');

  busyText(): string {
    switch (this.busy()) {
      case 'label':
        return 'Reading label…';
      case 'barcode':
        return 'Reading barcode…';
      default:
        return 'Saving…';
    }
  }

  onLabelPhoto(event: Event): void {
    const file = this.takeFile(event);
    if (!file) return;
    this.busy.set('label');
    this.shopping
      .uploadLabel(this.data.householdId, this.data.ingredientId, file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (scan) => this.sheetRef.dismiss({ action: 'label', scan }),
        error: (err: unknown) => this.fail(uploadErrorMessage(err, LABEL_UNREADABLE)),
      });
  }

  async onBarcodePhoto(event: Event): Promise<void> {
    const file = this.takeFile(event);
    if (!file) return;
    this.busy.set('barcode');
    const upc = await this.decoder.decode(file);
    const request = upc
      ? this.shopping.submitBarcode(this.data.householdId, this.data.ingredientId, upc)
      : this.shopping.decodeBarcodeImage(this.data.householdId, this.data.ingredientId, file);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result: BarcodeResult) => {
        if (result.decoded && result.upc) this.sheetRef.dismiss({ action: 'barcode', result });
        else this.fail(NO_BARCODE_FOUND);
      },
      error: (err: unknown) => this.fail(uploadErrorMessage(err, NO_BARCODE_FOUND)),
    });
  }

  notFood(): void {
    this.busy.set('not-food');
    this.error.set('');
    this.shopping
      .markNotFood(this.data.householdId, this.data.ingredientId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.sheetRef.dismiss({ action: 'not-food', result }),
        error: () => this.fail(NOT_FOOD_FAILED),
      });
  }

  skip(): void {
    this.sheetRef.dismiss({ action: 'skip' });
  }

  close(): void {
    this.sheetRef.dismiss();
  }

  private takeFile(event: Event): File | null {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    this.error.set('');
    if (!file) return null;
    if (file.size > MAX_PHOTO_BYTES) {
      this.error.set(PHOTO_TOO_LARGE);
      return null;
    }
    return file;
  }

  private fail(message: string): void {
    this.busy.set('');
    this.error.set(message);
  }
}
