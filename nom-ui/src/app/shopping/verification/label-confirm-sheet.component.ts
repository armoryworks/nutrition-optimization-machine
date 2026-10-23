import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
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
import { revealFirstInvalid } from '../../core/utils/reveal-invalid-control';

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

/**
 * The confirmation body: only rows the user left a number in are sent, on the
 * basis the label was printed in. A per-100 g table carries no serving weight,
 * because nothing should ever scale those values.
 */
export function labelConfirmBody(
  value: LabelFormValue,
  parsed: NutritionFactsParseResult,
): IngredientVerificationConfirm {
  const per100g = parsed.basis === 'per100g';
  const nutrients = LABEL_ROWS.flatMap((row) => {
    const amount = numberOrNull(value.nutrients[row.key]);
    if (amount === null) return [];
    const printed = parsed.nutrients.find((n) => n.key === row.key);
    return [{ key: row.key, amount, unit: row.unit, percentDailyValue: printed?.percentDailyValue ?? null }];
  });
  return {
    basis: per100g ? 'per100g' : 'serving',
    servingText: value.servingText?.trim() || null,
    servingGrams: per100g ? null : numberOrNull(value.servingGrams),
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
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly rows = LABEL_ROWS;
  readonly parsed = this.data.scan.parsed;
  readonly per100g = this.parsed.basis === 'per100g';
  saving = signal(false);
  error = signal('');
  invalid = signal(false);

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
    return this.per100g
      ? 'Values are per 100 g, exactly as printed'
      : 'Values are per serving, exactly as printed';
  }

  /** What the label printed where the parser could not read a number: "< 1 g", or "not a significant source". */
  printedHint(key: string): string {
    const reading = this.parsed.nutrients.find((n) => n.key === key);
    if (reading?.lessThan != null && reading.amount == null) {
      return `Label says less than ${reading.lessThan} ${reading.unit} — leave blank to keep that`;
    }
    if (this.parsed.notSignificantSource.includes(key)) return 'Label says not a significant source';
    return '';
  }

  lessThanPlaceholder(key: string): string {
    const reading = this.parsed.nutrients.find((n) => n.key === key);
    return reading?.lessThan != null && reading.amount == null ? `< ${reading.lessThan}` : '';
  }

  onFieldFocus(event: FocusEvent): void {
    keepFieldVisible(event.target, window);
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      this.invalid.set(true);
      revealFirstInvalid(this.form, this.host.nativeElement);
      return;
    }
    this.invalid.set(false);
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
