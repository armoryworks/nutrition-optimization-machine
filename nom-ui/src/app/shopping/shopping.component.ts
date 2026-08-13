import {
  Component,
  inject,
  signal,
  computed,
  OnInit,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { NoHouseholdCta } from '../shared/components/no-household-cta/no-household-cta.component';
import { toLocalDateString } from '../core/utils/local-date';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MealPlanService } from '../core/services/meal-plan.service';
import { HouseholdStore } from '../core/services/household-store';
import { RecipeService } from '../core/services/recipe.service';
import { PantryService } from '../core/services/pantry.service';
import { RetailPackagingService } from '../core/services/retail-packaging.service';
import { MeasurementService } from '../core/services/measurement.service';
import { MealPlanWeekResponse } from '../core/models/meal-plan-week-response.model';
import { RecipeModel } from '../core/models/recipe.model';
import { PantryItemResponse } from '../core/models/pantry-item-response.model';
import { PantryItemCreateRequest } from '../core/models/pantry-item-create-request.model';
import { RetailPackagingResponse } from '../core/models/retail-packaging-response.model';
import {
  ShoppingPortion,
  ShoppingItem,
  ShoppingDepartment,
} from '../core/domain/shopping/shopping-types';
import {
  DEPARTMENT_ORDER,
  DEPARTMENT_ICONS,
  categorizeDepartment,
} from '../core/domain/shopping/departments';
import { SHELF_LIFE_DEFAULTS } from '../core/domain/shopping/shelf-life';
import {
  UnitCategory,
  getUnitInfo,
  isLiquid,
  isFreshHerb,
  getIngredientDensity,
  toWeightDisplay,
  toVolumeDisplay,
  toLiquidDisplay,
  findRetailPackage,
  formatRetailPortion,
  isSmallVolumeItem,
  toSmallVolumeDisplay,
  formatQuantity,
} from '../core/domain/shopping/unit-conversion';

/** Accumulator used during ingredient merging */
interface RawAccumulator {
  ingredientId: number;
  name: string;
  baseQuantity: number;
  category: UnitCategory;
  originalUnit: string;
  department: string;
}

@Component({
  selector: 'nom-shopping',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule, ErrorBanner, NoHouseholdCta],
  templateUrl: './shopping.component.html',
  styleUrl: './shopping.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShoppingComponent implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private householdStore = inject(HouseholdStore);
  private recipeService = inject(RecipeService);
  private pantryService = inject(PantryService);
  private retailPackagingService = inject(RetailPackagingService);
  private measurementService = inject(MeasurementService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  householdId = signal(0);
  daysAhead = signal(4);
  weekDataList = signal<MealPlanWeekResponse[]>([]);
  recipeCache = signal<Map<number, RecipeModel>>(new Map());
  pantryItems = signal<PantryItemResponse[]>([]);
  retailPackages = signal<RetailPackagingResponse[]>([]);
  lookingUpPackaging = signal(false);
  loading = signal(true);
  error = signal('');
  /** Household-absence is a setup state, not an error — it gets the shared CTA. */
  noHousehold = signal(false);
  checkedItems = signal<Set<string>>(new Set());

  // Inline quantity editing
  editingItem = signal<string | null>(null);
  quantityOverrides = signal<Map<string, string>>(new Map());

  // Complete trip state
  completingTrip = signal(false);
  measurements = signal<{ id: number; name: string; symbol: string }[]>([]);

  departments = computed<ShoppingDepartment[]>(() => {
    const weeks = this.weekDataList();
    const cache = this.recipeCache();
    const packages = this.retailPackages();
    if (weeks.length === 0 || cache.size === 0) return [];

    const today = ShoppingComponent.toDateString(new Date());
    const endDate = ShoppingComponent.toDateString(
      ShoppingComponent.addDays(new Date(), this.daysAhead()),
    );

    // Accumulate ingredients in base units, merging by ingredientId + unit category
    const accMap = new Map<string, RawAccumulator>();

    for (const week of weeks) {
      for (const day of week.days) {
        if (day.date < today || day.date >= endDate) continue;

        for (const cell of day.cells) {
          for (const entry of cell.entries) {
            if (!entry.recipeId) continue;
            const recipe = cache.get(entry.recipeId);
            if (!recipe?.ingredients?.length) continue;

            for (const ing of recipe.ingredients) {
              const info = getUnitInfo(ing.measurement ?? '');
              const key = `${ing.ingredientId}-${info.category}`;
              const baseQty = ing.quantity * info.toBase;

              const existing = accMap.get(key);
              if (existing) {
                existing.baseQuantity += baseQty;
              } else {
                accMap.set(key, {
                  ingredientId: ing.ingredientId,
                  name: ing.name,
                  baseQuantity: baseQty,
                  category: info.category,
                  originalUnit: ing.measurement ?? '',
                  department: categorizeDepartment(ing.name),
                });
              }
            }
          }
        }
      }
    }

    // Subtract pantry stock (active, non-expired items)
    const pantry = this.pantryItems();
    for (const p of pantry) {
      if (p.isExpired || p.statusName !== 'In Pantry') continue;

      const info = getUnitInfo(p.measurementName);

      // Find matching accumulator (same ingredient + same unit category)
      const key = `${p.ingredientId}-${info.category}`;
      const acc = accMap.get(key);
      if (acc) {
        const pantryBase = p.quantity * info.toBase;
        acc.baseQuantity -= pantryBase;
      }
    }

    // Group by ingredientId to merge entries across unit categories
    // (e.g., Cherry Tomato measured in both cups and count → one line)
    const ingredientMap = new Map<number, RawAccumulator[]>();

    for (const acc of accMap.values()) {
      if (acc.baseQuantity <= 0) continue;
      const list = ingredientMap.get(acc.ingredientId) ?? [];
      list.push(acc);
      ingredientMap.set(acc.ingredientId, list);
    }

    // Convert to shopping items — use retail packaging when available
    const deptMap = new Map<string, ShoppingItem[]>();

    for (const [ingredientId, accs] of ingredientMap) {
      const name = accs[0].name;
      const dept = accs[0].department;
      const portions: ShoppingPortion[] = [];

      // Gather raw totals by category (in base units: ml, g, count)
      let totalMassG = 0;
      let totalVolumeMl = 0;
      let totalCount = 0;

      for (const acc of accs) {
        if (acc.category === 'mass') totalMassG += acc.baseQuantity;
        else if (acc.category === 'volume') totalVolumeMl += acc.baseQuantity;
        else if (acc.category === 'count') totalCount += acc.baseQuantity;
        else totalCount += acc.baseQuantity; // 'other' → treat as count
      }

      // When count coexists with mass/volume, fold count into mass
      // (e.g., "8 piece" + "14 oz" spaghetti → treat 8 count as ~8 servings of weight)
      // Only keep count standalone for naturally countable items (eggs, tortillas, etc.)
      const isCountable =
        /\b(egg|tortilla|wrap|pita|naan|bun|roll|bagel|muffin|slice|sheet|leaf)\b/i.test(name);
      if (totalCount > 0 && (totalMassG > 0 || totalVolumeMl > 0) && !isCountable) {
        // Fold count into mass — assume 1 count ≈ 1 serving (~100g for most ingredients)
        totalMassG += totalCount * 100;
        totalCount = 0;
      }

      // Try retail packaging — find best match regardless of unit category,
      // then convert recipe amounts to the package's category via density.
      let handledByRetail = false;
      let retailPkgCount = 0;
      const pkg = findRetailPackage(name, packages);

      if (pkg) {
        const density = getIngredientDensity(name);
        let totalBase: number;

        if (pkg.sizeCategory === 'mass') {
          // Convert everything to grams to match mass-based package
          totalBase =
            totalMassG +
            totalVolumeMl * density +
            (totalCount > 0 && !isCountable ? totalCount * 100 : 0);
        } else if (pkg.sizeCategory === 'volume') {
          // Convert everything to ml to match volume-based package
          totalBase =
            totalVolumeMl +
            (density > 0 ? totalMassG / density : 0) +
            (totalCount > 0 && !isCountable ? totalCount * 236.588 : 0);
        } else {
          // Count-based package
          totalBase = totalCount;
        }

        if (totalBase > 0) {
          retailPkgCount = Math.ceil(totalBase / pkg.sizeInBaseUnits);
          portions.push(formatRetailPortion(pkg, retailPkgCount));
          handledByRetail = true;
          totalMassG = 0;
          totalVolumeMl = 0;
          if (pkg.sizeCategory !== 'count') totalCount = isCountable ? totalCount : 0;
          else totalCount = 0;
        }
      }

      // Fallback for remaining count (standalone countable items like eggs)
      if (totalCount > 0) {
        portions.push({ quantity: Math.ceil(totalCount), unit: '' });
      }

      if (!handledByRetail) {
        if (isFreshHerb(name, dept)) {
          if (totalVolumeMl > 0 || totalMassG > 0) {
            const totalG = totalMassG + totalVolumeMl * 0.15;
            portions.push({ quantity: Math.max(1, Math.ceil(totalG / 28)), unit: 'bunch' });
          }
        } else if (isSmallVolumeItem(name) && totalVolumeMl > 0) {
          // Spices/seasonings: keep in tsp/tbsp (not oz)
          portions.push(toSmallVolumeDisplay(totalVolumeMl));
        } else if (isLiquid(name)) {
          let totalMl = totalVolumeMl;
          if (totalMassG > 0) totalMl += totalMassG;
          if (totalMl > 0) {
            portions.push(toLiquidDisplay(totalMl));
          }
        } else {
          // Combine mass + volume via density
          let totalG = totalMassG;
          if (totalVolumeMl > 0) {
            totalG += totalVolumeMl * getIngredientDensity(name);
          }
          if (totalG > 0) {
            // For very small amounts (< 2 oz), show in recipe-friendly volume
            // units if the original was volume (more useful than "1/2 oz")
            if (totalG < 57 && totalVolumeMl > 0 && totalMassG === 0) {
              portions.push(toVolumeDisplay(totalVolumeMl));
            } else {
              portions.push(toWeightDisplay(totalG));
            }
          }
        }
      }

      if (portions.length === 0) continue;

      const item: ShoppingItem = {
        ingredientId,
        name,
        portions,
        department: dept,
        checkKey: `${ingredientId}`,
        baseMassG: accs
          .filter((a) => a.category === 'mass')
          .reduce((s, a) => s + a.baseQuantity, 0),
        baseVolumeMl: accs
          .filter((a) => a.category === 'volume')
          .reduce((s, a) => s + a.baseQuantity, 0),
        baseCount: accs
          .filter((a) => a.category === 'count' || a.category === 'other')
          .reduce((s, a) => s + a.baseQuantity, 0),
        retailPackage: handledByRetail ? pkg : null,
        retailPackageCount: retailPkgCount,
      };

      const items = deptMap.get(dept) ?? [];
      items.push(item);
      deptMap.set(dept, items);
    }

    // Sort departments by store-aisle order, items alphabetically within
    const result: ShoppingDepartment[] = [];
    for (const deptName of DEPARTMENT_ORDER) {
      const items = deptMap.get(deptName);
      if (items && items.length > 0) {
        items.sort((a, b) => a.name.localeCompare(b.name));
        result.push({
          name: deptName,
          icon: DEPARTMENT_ICONS[deptName] ?? 'category',
          items,
        });
      }
    }

    return result;
  });

  totalItemCount = computed(() =>
    this.departments().reduce((sum, dept) => sum + dept.items.length, 0),
  );

  checkedCount = computed(() => this.checkedItems().size);

  // --- Inline quantity editing ---

  startEditing(checkKey: string, currentText: string): void {
    this.editingItem.set(checkKey);
    // Pre-populate the override with the current displayed text
    const overrides = new Map(this.quantityOverrides());
    if (!overrides.has(checkKey)) {
      overrides.set(checkKey, currentText);
      this.quantityOverrides.set(overrides);
    }
  }

  saveEdit(checkKey: string, value: string): void {
    const trimmed = value.trim();
    if (trimmed) {
      const overrides = new Map(this.quantityOverrides());
      overrides.set(checkKey, trimmed);
      this.quantityOverrides.set(overrides);
    }
    this.editingItem.set(null);
  }

  cancelEdit(): void {
    this.editingItem.set(null);
  }

  // --- Share / Export ---

  exportList(format: 'text' | 'csv'): void {
    const departments = this.departments();
    if (departments.length === 0) return;

    if (format === 'csv') {
      const lines = ['Department,Item,Quantity'];
      for (const dept of departments) {
        for (const item of dept.items) {
          const qty = this.getDisplayText(item).replace(/,/g, ';');
          lines.push(`"${dept.name}","${item.name}","${qty}"`);
        }
      }
      const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `shopping-list-${toLocalDateString(new Date())}.csv`;
      a.click();
      URL.revokeObjectURL(url);
      this.snackBar.open('CSV downloaded', 'OK', { duration: 2000 });
    } else {
      const lines: string[] = [];
      for (const dept of departments) {
        lines.push(`\n${dept.name}`);
        lines.push('\u2500'.repeat(dept.name.length));
        for (const item of dept.items) {
          const qty = this.getDisplayText(item);
          lines.push(`  \u25A1 ${item.name}  ${qty}`);
        }
      }
      const text = `Shopping List \u2013 ${new Date().toLocaleDateString()}\n${'\u2550'.repeat(30)}${lines.join('\n')}`;
      navigator.clipboard.writeText(text).then(() => {
        this.snackBar.open('Copied to clipboard', 'OK', { duration: 2000 });
      });
    }
  }

  shareList(): void {
    const departments = this.departments();
    if (departments.length === 0) return;

    const lines: string[] = [`Shopping List \u2013 ${new Date().toLocaleDateString()}\n`];
    for (const dept of departments) {
      lines.push(`${dept.name}:`);
      for (const item of dept.items) {
        lines.push(`  \u2022 ${item.name} \u2013 ${this.getDisplayText(item)}`);
      }
      lines.push('');
    }
    const text = lines.join('\n');

    if (navigator.share) {
      navigator.share({ title: 'Shopping List', text }).catch(() => {
        // Fallback to clipboard
        navigator.clipboard.writeText(text).then(() => {
          this.snackBar.open('Copied to clipboard', 'OK', { duration: 2000 });
        });
      });
    } else {
      navigator.clipboard.writeText(text).then(() => {
        this.snackBar.open('Copied to clipboard', 'OK', { duration: 2000 });
      });
    }
  }

  getDisplayText(item: ShoppingItem): string {
    const override = this.quantityOverrides().get(item.checkKey);
    if (override) return override;
    // Build default text from portions
    return item.portions
      .map((p) => {
        const qty = p.quantity > 0 ? this.formatQuantity(p.quantity) + ' ' : '';
        return qty + p.unit;
      })
      .join(' + ');
  }

  hasOverride(checkKey: string): boolean {
    return this.quantityOverrides().has(checkKey);
  }

  // --- Complete Shopping Trip ---

  completeTrip(): void {
    if (this.completingTrip()) return;

    const checked = this.checkedItems();
    if (checked.size === 0) return;

    const departments = this.departments();
    const allMeasurements = this.measurements();

    // Find measurement IDs by name
    const gramId = allMeasurements.find((m) => m.name.toLowerCase() === 'gram')?.id;
    const mlId = allMeasurements.find((m) => m.name.toLowerCase() === 'milliliter')?.id;
    const pieceId = allMeasurements.find((m) => m.name.toLowerCase() === 'piece')?.id;

    if (!gramId || !mlId || !pieceId) {
      this.snackBar.open('Measurement data not loaded. Please refresh and try again.', 'OK', {
        duration: 4000,
      });
      return;
    }

    const today = new Date();
    const todayStr = toLocalDateString(today);
    const items: PantryItemCreateRequest[] = [];

    for (const dept of departments) {
      for (const item of dept.items) {
        if (!checked.has(item.checkKey)) continue;

        // Determine quantity and measurement based on override or original data
        const override = this.quantityOverrides().get(item.checkKey);
        let quantity: number;
        let measurementId: number;

        if (override) {
          // Parse override: user may type "6 cans", "2 lb", "500 g", etc.
          const parsed = this.parseOverride(override, item);
          quantity = parsed.quantity;
          measurementId =
            parsed.measurementId ?? this.pickBestMeasurement(item, gramId, mlId, pieceId);
        } else if (item.retailPackage && item.retailPackageCount > 0) {
          // Use retail package: pkgCount × package base units
          const totalBase = item.retailPackageCount * item.retailPackage.sizeInBaseUnits;
          if (item.retailPackage.sizeCategory === 'mass') {
            quantity = totalBase;
            measurementId = gramId;
          } else if (item.retailPackage.sizeCategory === 'volume') {
            quantity = totalBase;
            measurementId = mlId;
          } else {
            quantity = totalBase;
            measurementId = pieceId;
          }
        } else {
          // Use raw base quantities
          if (item.baseMassG > 0) {
            quantity = Math.round(item.baseMassG * 100) / 100;
            measurementId = gramId;
          } else if (item.baseVolumeMl > 0) {
            quantity = Math.round(item.baseVolumeMl * 100) / 100;
            measurementId = mlId;
          } else {
            quantity = Math.max(1, Math.ceil(item.baseCount));
            measurementId = pieceId;
          }
        }

        if (quantity <= 0) continue;

        // Shelf life from department
        const shelfLife = SHELF_LIFE_DEFAULTS[dept.name.toLowerCase()] ?? 90;
        const expDate = new Date(today);
        expDate.setDate(expDate.getDate() + shelfLife);

        items.push({
          householdId: this.householdId(),
          ingredientId: item.ingredientId,
          quantity,
          measurementId,
          acquisitionDate: todayStr,
          expectedExpirationDate: toLocalDateString(expDate),
        });
      }
    }

    if (items.length === 0) {
      this.snackBar.open('No valid items to transfer.', 'OK', { duration: 3000 });
      return;
    }

    this.completingTrip.set(true);
    this.pantryService
      .addPantryItemsBatch(items)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.snackBar.open(`${items.length} item(s) added to pantry!`, 'OK', { duration: 3000 });
          // Clear checked items and overrides
          this.checkedItems.set(new Set());
          this.quantityOverrides.set(new Map());
          this.saveCheckedState();
          this.completingTrip.set(false);
          // Reload to reflect pantry deductions
          this.loading.set(true);
          this.loadData();
        },
        error: () => {
          this.snackBar.open('Failed to add items to pantry.', 'OK', { duration: 4000 });
          this.completingTrip.set(false);
        },
      });
  }

  private parseOverride(
    text: string,
    item: ShoppingItem,
  ): { quantity: number; measurementId: number | null } {
    const allMeasurements = this.measurements();
    // Try to parse patterns like "6 cans", "2.5 lb", "500 g", "3"
    const match = text.match(/^(\d+(?:\.\d+)?)\s*(.*)?$/);
    if (!match) return { quantity: 1, measurementId: null };

    const num = parseFloat(match[1]);
    const unitText = (match[2] || '').trim().toLowerCase();

    if (!unitText) {
      // Just a number — scale based on original retail package
      if (item.retailPackage && item.retailPackageCount > 0) {
        const totalBase = num * item.retailPackage.sizeInBaseUnits;
        const gramId = allMeasurements.find((m) => m.name.toLowerCase() === 'gram')?.id;
        const mlId = allMeasurements.find((m) => m.name.toLowerCase() === 'milliliter')?.id;
        const pieceId = allMeasurements.find((m) => m.name.toLowerCase() === 'piece')?.id;
        if (item.retailPackage.sizeCategory === 'mass')
          return { quantity: totalBase, measurementId: gramId ?? null };
        if (item.retailPackage.sizeCategory === 'volume')
          return { quantity: totalBase, measurementId: mlId ?? null };
        return { quantity: totalBase, measurementId: pieceId ?? null };
      }
      return { quantity: num, measurementId: null };
    }

    // Try to match unit text to known measurements
    const unitMap: Record<string, string> = {
      g: 'gram',
      gram: 'gram',
      grams: 'gram',
      oz: 'ounce',
      ounce: 'ounce',
      ounces: 'ounce',
      lb: 'pound',
      lbs: 'pound',
      pound: 'pound',
      pounds: 'pound',
      kg: 'kilogram',
      kilogram: 'kilogram',
      kilograms: 'kilogram',
      ml: 'milliliter',
      milliliter: 'milliliter',
      milliliters: 'milliliter',
      l: 'liter',
      liter: 'liter',
      liters: 'liter',
      cup: 'cup',
      cups: 'cup',
      tbsp: 'tablespoon',
      tablespoon: 'tablespoon',
      tablespoons: 'tablespoon',
      tsp: 'teaspoon',
      teaspoon: 'teaspoon',
      teaspoons: 'teaspoon',
      piece: 'piece',
      pieces: 'piece',
      each: 'piece',
      dozen: 'dozen',
    };

    const mappedName = unitMap[unitText];
    if (mappedName) {
      const meas = allMeasurements.find((m) => m.name.toLowerCase() === mappedName);
      if (meas) {
        // Convert to base units for pantry storage
        const info = getUnitInfo(mappedName);
        return {
          quantity: num * info.toBase,
          measurementId:
            allMeasurements.find(
              (m) =>
                m.name.toLowerCase() ===
                (info.category === 'mass'
                  ? 'gram'
                  : info.category === 'volume'
                    ? 'milliliter'
                    : 'piece'),
            )?.id ?? null,
        };
      }
    }

    // If unit is a package name (can, box, bottle, bag, jar, tub, bunch, etc.)
    if (
      /^(cans?|boxes?|bottles?|bags?|jars?|tubs?|bunch|bunches|cartons?|packs?|containers?)$/.test(
        unitText,
      )
    ) {
      // Use the number × retail package size if available
      if (item.retailPackage) {
        const totalBase = num * item.retailPackage.sizeInBaseUnits;
        const gramId = allMeasurements.find((m) => m.name.toLowerCase() === 'gram')?.id;
        const mlId = allMeasurements.find((m) => m.name.toLowerCase() === 'milliliter')?.id;
        const pieceId = allMeasurements.find((m) => m.name.toLowerCase() === 'piece')?.id;
        if (item.retailPackage.sizeCategory === 'mass')
          return { quantity: totalBase, measurementId: gramId ?? null };
        if (item.retailPackage.sizeCategory === 'volume')
          return { quantity: totalBase, measurementId: mlId ?? null };
        return { quantity: totalBase, measurementId: pieceId ?? null };
      }
    }

    // Fallback: just use the number
    return { quantity: num, measurementId: null };
  }

  private pickBestMeasurement(
    item: ShoppingItem,
    gramId: number,
    mlId: number,
    pieceId: number,
  ): number {
    if (item.baseMassG > 0) return gramId;
    if (item.baseVolumeMl > 0) return mlId;
    return pieceId;
  }

  ngOnInit(): void {
    this.householdStore
      .getHouseholds()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          if (list.length > 0) {
            this.noHousehold.set(false);
            this.householdId.set(list[0].id);
            this.loadData();
          } else {
            this.loading.set(false);
            this.noHousehold.set(true);
          }
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Failed to load household.');
        },
      });
  }

  onDaysChange(event: Event): void {
    const value = parseInt((event.target as HTMLInputElement).value, 10);
    if (value >= 1 && value <= 14) {
      this.daysAhead.set(value);
      this.loading.set(true);
      this.loadData();
    }
  }

  refresh(): void {
    this.loading.set(true);
    this.loadData();
  }

  /** The no-household CTA created a kitchen — reload page state in place. */
  onHouseholdCreated(): void {
    this.loading.set(true);
    this.householdStore
      .getHouseholds()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          if (list.length > 0) {
            this.noHousehold.set(false);
            this.householdId.set(list[0].id);
            this.loadData();
          } else {
            this.loading.set(false);
          }
        },
        error: () => this.loading.set(false),
      });
  }

  isChecked(key: string): boolean {
    return this.checkedItems().has(key);
  }

  toggleChecked(key: string): void {
    const checked = new Set(this.checkedItems());
    if (checked.has(key)) {
      checked.delete(key);
    } else {
      checked.add(key);
    }
    this.checkedItems.set(checked);
    this.saveCheckedState();
  }

  formatQuantity(qty: number): string {
    return formatQuantity(qty);
  }

  private loadData(): void {
    this.error.set('');
    const today = new Date();
    const endDate = ShoppingComponent.addDays(today, this.daysAhead());
    const monday1 = ShoppingComponent.getMonday(today);
    const monday2 = ShoppingComponent.getMonday(endDate);

    const weekFetches = [this.mealPlanService.getWeek(this.householdId(), monday1)];
    if (monday2 !== monday1) {
      weekFetches.push(this.mealPlanService.getWeek(this.householdId(), monday2));
    }

    // Fetch meal plan weeks, pantry items, retail packaging, and measurements in parallel
    forkJoin({
      weeks: forkJoin(weekFetches),
      pantry: this.pantryService.getPantryItems(this.householdId()),
      packaging: this.retailPackagingService.getAll(),
      measurements:
        this.measurements().length > 0
          ? of(this.measurements())
          : this.measurementService.loadMeasurements(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ weeks, pantry, packaging, measurements }) => {
          this.weekDataList.set(weeks);
          this.pantryItems.set(pantry);
          this.retailPackages.set(packaging);
          this.measurements.set(measurements);
          this.loadRecipes(weeks);
        },
        error: () => {
          this.error.set('Failed to load meal plan data.');
          this.loading.set(false);
        },
      });
  }

  private loadRecipes(weeks: MealPlanWeekResponse[]): void {
    const today = ShoppingComponent.toDateString(new Date());
    const endDate = ShoppingComponent.toDateString(
      ShoppingComponent.addDays(new Date(), this.daysAhead()),
    );

    const recipeIds = new Set<number>();
    for (const week of weeks) {
      for (const day of week.days) {
        if (day.date < today || day.date >= endDate) continue;
        for (const cell of day.cells) {
          for (const entry of cell.entries) {
            if (entry.recipeId) recipeIds.add(entry.recipeId);
          }
        }
      }
    }

    if (recipeIds.size === 0) {
      this.recipeCache.set(new Map());
      this.loading.set(false);
      this.loadCheckedState();
      return;
    }

    forkJoin(Array.from(recipeIds).map((id) => this.recipeService.getRecipe(id)))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (recipes) => {
          const cache = new Map<number, RecipeModel>();
          for (const recipe of recipes) {
            cache.set(recipe.id, recipe);
          }
          this.recipeCache.set(cache);
          this.loading.set(false);
          this.loadCheckedState();
          this.lookupMissingPackaging();
        },
        error: () => {
          this.error.set('Failed to load recipe details.');
          this.loading.set(false);
        },
      });
  }

  private lookupMissingPackaging(): void {
    const departments = this.departments();
    const packages = this.retailPackages();

    // Collect ingredient names that have no retail packaging match
    const unmatchedNames: string[] = [];
    for (const dept of departments) {
      for (const item of dept.items) {
        const hasMatch = findRetailPackage(item.name, packages);
        if (!hasMatch) {
          unmatchedNames.push(item.name);
        }
      }
    }

    if (unmatchedNames.length === 0) return;

    this.lookingUpPackaging.set(true);
    this.retailPackagingService
      .lookup(unmatchedNames)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (response.results.length > 0) {
            // Merge new results into existing packages
            const merged = [...this.retailPackages(), ...response.results];
            this.retailPackages.set(merged);
          }
          this.lookingUpPackaging.set(false);
        },
        error: () => {
          // Silently fail — list is already usable with fallback display
          this.lookingUpPackaging.set(false);
        },
      });
  }

  private get storageKey(): string {
    const today = ShoppingComponent.toDateString(new Date());
    return `nom-shopping-${this.householdId()}-${today}-${this.daysAhead()}`;
  }

  private loadCheckedState(): void {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored) {
        this.checkedItems.set(new Set(JSON.parse(stored) as string[]));
      } else {
        this.checkedItems.set(new Set());
      }
    } catch {
      this.checkedItems.set(new Set());
    }
  }

  private saveCheckedState(): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify([...this.checkedItems()]));
    } catch {
      /* localStorage unavailable */
    }
  }

  static getMonday(date: Date): string {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return ShoppingComponent.toDateString(d);
  }

  static addDays(date: Date, days: number): Date {
    const d = new Date(date);
    d.setDate(d.getDate() + days);
    return d;
  }

  static toDateString(date: Date): string {
    return toLocalDateString(date);
  }
}
