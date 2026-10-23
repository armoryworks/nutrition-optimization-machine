import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { HouseholdShoppingService } from '../../core/services/household-shopping.service';
import { UnitPreferenceService } from '../../core/services/unit-preference.service';
import { IngredientMeasurements, PackageUnit } from '../../core/models/household-shopping.model';
import { UnitSystem } from '../../core/utils/mass-display';
import { keepFieldVisible } from '../../core/utils/keyboard-room';

export interface MeasurementsSheetData {
  ingredientId: number;
  householdId: number;
  name: string;
}

export type MeasurementsResult = { saved: true } | undefined;

export const PACKAGE_UNITS: readonly { value: PackageUnit; label: string; system: UnitSystem }[] = [
  { value: 'g', label: 'g', system: 'metric' },
  { value: 'kg', label: 'kg', system: 'metric' },
  { value: 'ml', label: 'ml', system: 'metric' },
  { value: 'l', label: 'L', system: 'metric' },
  { value: 'oz', label: 'oz', system: 'imperial' },
  { value: 'lb', label: 'lb', system: 'imperial' },
  { value: 'floz', label: 'fl oz', system: 'imperial' },
];

export function defaultPackageUnit(system: UnitSystem): PackageUnit {
  return system === 'imperial' ? 'oz' : 'g';
}

export interface MeasurementsFormValue {
  packageAmount: number | null;
  packageUnit: PackageUnit;
  piecesPerPackage: number | null;
  cupGrams: number | null;
}

function positive(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) && n > 0 ? n : null;
}

/** The request for what was filled in, or null when every field is still blank. */
export function measurementsBody(
  householdId: number,
  value: MeasurementsFormValue,
): IngredientMeasurements | null {
  const packageAmount = positive(value.packageAmount);
  const piecesPerPackage = positive(value.piecesPerPackage);
  const cupGrams = positive(value.cupGrams);
  if (packageAmount === null && piecesPerPackage === null && cupGrams === null) return null;
  return {
    householdId,
    packageAmount,
    packageUnit: packageAmount === null ? null : value.packageUnit,
    piecesPerPackage,
    cupGrams,
  };
}

@Component({
  selector: 'nom-measurements-sheet',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './measurements-sheet.component.html',
  styleUrl: './measurements-sheet.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MeasurementsSheet {
  readonly data = inject<MeasurementsSheetData>(MAT_BOTTOM_SHEET_DATA);
  private sheetRef = inject(MatBottomSheetRef<MeasurementsSheet, MeasurementsResult>);
  private shopping = inject(HouseholdShoppingService);
  private units = inject(UnitPreferenceService);
  private destroyRef = inject(DestroyRef);

  readonly packageUnits = PACKAGE_UNITS;
  saving = signal(false);
  error = signal('');

  readonly form = new FormGroup({
    packageAmount: new FormControl<number | null>(null, [Validators.min(0)]),
    packageUnit: new FormControl<PackageUnit>(defaultPackageUnit(this.units.system()), { nonNullable: true }),
    piecesPerPackage: new FormControl<number | null>(null, [Validators.min(1), Validators.pattern(/^\d+$/)]),
    cupGrams: new FormControl<number | null>(null, [Validators.min(0)]),
  });

  private value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  canSave(): boolean {
    this.value();
    return this.form.valid && measurementsBody(this.data.householdId, this.form.getRawValue()) !== null;
  }

  onFieldFocus(event: FocusEvent): void {
    keepFieldVisible(event.target, window);
  }

  save(): void {
    const body = measurementsBody(this.data.householdId, this.form.getRawValue());
    if (this.saving() || this.form.invalid || !body) return;
    this.saving.set(true);
    this.error.set('');
    this.shopping
      .saveMeasurements(this.data.ingredientId, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.sheetRef.dismiss({ saved: true }),
        error: () => {
          this.saving.set(false);
          this.error.set('Could not save those measurements — check your connection and try again');
        },
      });
  }

  skip(): void {
    this.sheetRef.dismiss();
  }
}
