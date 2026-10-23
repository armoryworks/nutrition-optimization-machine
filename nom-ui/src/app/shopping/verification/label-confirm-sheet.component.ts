import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, FormRecord, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { HouseholdShoppingService } from '../../core/services/household-shopping.service';
import {
  IngredientVerificationConfirm,
  LabelNutrientUnit,
  LabelScanResult,
  NutritionFactsParseResult,
} from '../../core/models/household-shopping.model';
import { keepFieldVisible } from '../../core/utils/keyboard-room';

export interface LabelConfirmSheetData {
  ingredientId: number;
  householdId: number;
  name: string;
  scan: LabelScanResult;
}

export type LabelConfirmResult = { saved: true } | { retake: true } | undefined;

export interface LabelRow {
  key: string;
  label: string;
  unit: LabelNutrientUnit;
}

/** The Nutrition Facts panel in print order; keys match the server's parser. */
export const LABEL_ROWS: readonly LabelRow[] = [
  { key: 'fat', label: 'Total Fat', unit: 'g' },
  { key: 'saturated_fat', label: 'Saturated Fat', unit: 'g' },
  { key: 'trans_fat', label: 'Trans Fat', unit: 'g' },
  { key: 'cholesterol', label: 'Cholesterol', unit: 'mg' },
  { key: 'sodium', label: 'Sodium', unit: 'mg' },
  { key: 'carbohydrate', label: 'Total Carbohydrate', unit: 'g' },
  { key: 'fiber', label: 'Dietary Fiber', unit: 'g' },
  { key: 'sugars', label: 'Total Sugars', unit: 'g' },
  { key: 'added_sugars', label: 'Added Sugars', unit: 'g' },
  { key: 'protein', label: 'Protein', unit: 'g' },
  { key: 'vitamin_d', label: 'Vitamin D', unit: 'mcg' },
  { key: 'calcium', label: 'Calcium', unit: 'mg' },
  { key: 'iron', label: 'Iron', unit: 'mg' },
  { key: 'potassium', label: 'Potassium', unit: 'mg' },
];

export interface LabelFormValue {
  servingText: string | null;
  servingGrams: number | null;
  servingsPerContainer: number | null;
  calories: number | null;
  nutrients: Record<string, number | null>;
}

function numberOrNull(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

/** What the form starts with: the parser's reading for each printed row, blank where it saw nothing. */
export function labelFormValue(parsed: NutritionFactsParseResult): LabelFormValue {
  const nutrients: Record<string, number | null> = {};
  for (const row of LABEL_ROWS) {
    nutrients[row.key] = parsed.nutrients.find((n) => n.key === row.key)?.amount ?? null;
  }
  return {
    servingText: parsed.servingText ?? null,
    servingGrams: parsed.servingGrams ?? null,
    servingsPerContainer: parsed.servingsPerContainer ?? null,
    calories: parsed.calories ?? null,
    nutrients,
  };
}

/** The confirmation body: only rows the user left a number in are sent. */
export function labelConfirmBody(
  value: LabelFormValue,
  parsed: NutritionFactsParseResult,
): IngredientVerificationConfirm {
  const nutrients = LABEL_ROWS.flatMap((row) => {
    const amount = numberOrNull(value.nutrients[row.key]);
    if (amount === null) return [];
    const printed = parsed.nutrients.find((n) => n.key === row.key);
    return [{ key: row.key, amount, unit: row.unit, percentDailyValue: printed?.percentDailyValue ?? null }];
  });
  return {
    servingText: value.servingText?.trim() || null,
    servingGrams: numberOrNull(value.servingGrams),
    servingMl: parsed.servingMl ?? null,
    servingsPerContainer: numberOrNull(value.servingsPerContainer),
    calories: numberOrNull(value.calories),
    nutrients,
  };
}

@Component({
  selector: 'nom-label-confirm-sheet',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './label-confirm-sheet.component.html',
  styleUrl: './label-confirm-sheet.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabelConfirmSheet {
  readonly data = inject<LabelConfirmSheetData>(MAT_BOTTOM_SHEET_DATA);
  private sheetRef = inject(MatBottomSheetRef<LabelConfirmSheet, LabelConfirmResult>);
  private shopping = inject(HouseholdShoppingService);
  private destroyRef = inject(DestroyRef);

  readonly rows = LABEL_ROWS;
  readonly parsed = this.data.scan.parsed;
  saving = signal(false);
  error = signal('');

  readonly nutrients = new FormRecord<FormControl<number | null>>({});
  readonly form = new FormGroup({
    servingText: new FormControl<string | null>(null),
    servingGrams: new FormControl<number | null>(null, [Validators.min(0.1)]),
    servingsPerContainer: new FormControl<number | null>(null, [Validators.min(0.1)]),
    calories: new FormControl<number | null>(null, [Validators.min(0)]),
    nutrients: this.nutrients,
  });

  constructor() {
    for (const row of LABEL_ROWS) {
      this.nutrients.addControl(row.key, new FormControl<number | null>(null, [Validators.min(0)]));
    }
    this.form.setValue(labelFormValue(this.parsed));
  }

  basisNote(): string {
    return this.parsed.basis === 'per100g'
      ? 'Values are per 100 g, exactly as printed'
      : 'Values are per serving, exactly as printed';
  }

  onFieldFocus(event: FocusEvent): void {
    keepFieldVisible(event.target, window);
  }

  save(): void {
    if (this.saving() || this.form.invalid) return;
    this.saving.set(true);
    this.error.set('');
    const body = labelConfirmBody(this.form.getRawValue() as LabelFormValue, this.parsed);
    this.shopping
      .confirmLabel(this.data.scan.verificationId, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.sheetRef.dismiss({ saved: true }),
        error: () => {
          this.saving.set(false);
          this.error.set('Could not save those values — check your connection and try again');
        },
      });
  }

  retake(): void {
    this.sheetRef.dismiss({ retake: true });
  }

  cancel(): void {
    this.sheetRef.dismiss();
  }
}
