import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
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
import { HouseholdService } from '../core/services/household.service';
import { RecipeService } from '../core/services/recipe.service';
import { PantryService } from '../core/services/pantry.service';
import { RetailPackagingService } from '../core/services/retail-packaging.service';
import { MeasurementService } from '../core/services/measurement.service';
import { MealPlanWeekResponse } from '../core/models/meal-plan-week-response.model';
import { RecipeModel } from '../core/models/recipe.model';
import { PantryItemResponse } from '../core/models/pantry-item-response.model';
import { PantryItemCreateRequest } from '../core/models/pantry-item-create-request.model';
import { RetailPackagingResponse } from '../core/models/retail-packaging-response.model';

export interface ShoppingPortion {
  quantity: number;
  unit: string;
}

export interface ShoppingItem {
  ingredientId: number;
  name: string;
  portions: ShoppingPortion[];
  department: string;
  checkKey: string;
  // Raw base quantities for pantry transfer
  baseMassG: number;
  baseVolumeMl: number;
  baseCount: number;
  // Retail package info for quantity override scaling
  retailPackage: RetailPackagingResponse | null;
  retailPackageCount: number;
}

export interface ShoppingDepartment {
  name: string;
  icon: string;
  items: ShoppingItem[];
}

// Department order for store-aisle flow
const DEPARTMENT_ORDER = [
  'Produce',
  'Meat & Seafood',
  'Dairy & Eggs',
  'Bakery',
  'Grains & Pasta',
  'Canned & Jarred',
  'Condiments & Sauces',
  'Spices & Seasonings',
  'Oils & Vinegars',
  'Baking',
  'Frozen',
  'Beverages',
  'Other',
];

const DEPARTMENT_ICONS: Record<string, string> = {
  'Produce': 'eco',
  'Meat & Seafood': 'set_meal',
  'Dairy & Eggs': 'egg',
  'Bakery': 'bakery_dining',
  'Grains & Pasta': 'grain',
  'Canned & Jarred': 'inventory_2',
  'Condiments & Sauces': 'local_dining',
  'Spices & Seasonings': 'spa',
  'Oils & Vinegars': 'water_drop',
  'Baking': 'cake',
  'Frozen': 'ac_unit',
  'Beverages': 'local_cafe',
  'Other': 'category',
};

/** Department-based default shelf life in days (reused from pantry component) */
const SHELF_LIFE_DEFAULTS: Record<string, number> = {
  'produce': 5,
  'meat & seafood': 3,
  'dairy & eggs': 10,
  'bakery': 5,
  'grains & pasta': 180,
  'canned & jarred': 365,
  'condiments & sauces': 90,
  'spices & seasonings': 365,
  'oils & vinegars': 180,
  'baking': 180,
  'frozen': 90,
  'beverages': 30,
  'other': 90,
};

// Unit conversion: recipe measurements → base units (ml for volume, g for mass)
type UnitCategory = 'volume' | 'mass' | 'count' | 'other';

interface UnitInfo {
  category: UnitCategory;
  toBase: number;
}

const UNIT_INFO: Record<string, UnitInfo> = {
  'teaspoon':     { category: 'volume', toBase: 4.929 },
  'tablespoon':   { category: 'volume', toBase: 14.787 },
  'fluid ounce':  { category: 'volume', toBase: 29.574 },
  'cup':          { category: 'volume', toBase: 236.588 },
  'milliliter':   { category: 'volume', toBase: 1 },
  'liter':        { category: 'volume', toBase: 1000 },
  'gram':         { category: 'mass', toBase: 1 },
  'ounce':        { category: 'mass', toBase: 28.3495 },
  'pound':        { category: 'mass', toBase: 453.592 },
  'kilogram':     { category: 'mass', toBase: 1000 },
  'piece':        { category: 'count', toBase: 1 },
  'each':         { category: 'count', toBase: 1 },
  'dozen':        { category: 'count', toBase: 12 },
};

function getUnitInfo(measurement: string): UnitInfo {
  return UNIT_INFO[measurement.toLowerCase()] ?? { category: 'other', toBase: 1 };
}

/** Round to the nearest fraction (e.g., nearest 1/4 for denominator=4) */
function roundToFraction(qty: number, denominator: number): number {
  return Math.round(qty * denominator) / denominator;
}

// ---- Ingredient classification for shopping-friendly display ----

/** Is this ingredient a pourable liquid? (Display in fl oz) */
function isLiquid(name: string): boolean {
  const n = name.toLowerCase();
  return /\b(milk|cream|buttermilk|half and half|broth|stock|juice|water|coconut milk|coconut cream|oil|vinegar|wine|beer|soda|kombucha|soy sauce|fish sauce|worcestershire|hot sauce|sriracha|teriyaki|hoisin|oyster sauce|honey|maple syrup|molasses|agave|lemon juice|lime juice|mirin|sake)\b/.test(n);
}

/** Is this a fresh herb typically sold by the bunch? */
function isFreshHerb(name: string, department: string): boolean {
  if (department !== 'Produce') return false;
  return /\b(cilantro|parsley|basil|mint|dill|rosemary|thyme|chives|oregano|sage|tarragon)\b/.test(name.toLowerCase());
}

/** Approximate density (g per ml) for converting recipe volume → shopping weight */
function getIngredientDensity(name: string): number {
  const n = name.toLowerCase();
  if (/cheese|parmesan|mozzarella|cheddar|feta|gouda|brie|ricotta|cream cheese/.test(n)) return 0.5;
  if (/yogurt|sour cream|kefir/.test(n)) return 1.03;
  if (/butter|ghee|margarine/.test(n)) return 0.91;
  if (/peanut butter|almond butter|nutella|tahini/.test(n)) return 1.1;
  if (/flour/.test(n)) return 0.53;
  if (/sugar/.test(n)) return 0.85;
  if (/rice|quinoa|couscous|barley|oat|granola|cereal|grain/.test(n)) return 0.75;
  if (/nut|almond|walnut|pecan|cashew|pistachio|seed/.test(n)) return 0.55;
  if (/spinach|kale|arugula|lettuce|greens|cabbage/.test(n)) return 0.15;
  if (/ginger/.test(n)) return 0.7;
  if (/cocoa|cornstarch|baking/.test(n)) return 0.55;
  if (/breadcrumb|panko|cracker/.test(n)) return 0.45;
  if (/olive|caper|pickle/.test(n)) return 0.65;
  return 0.6;
}

/** Convert grams to the best weight display (oz or lb) */
function toWeightDisplay(grams: number): ShoppingPortion {
  if (grams >= 453.592) {
    return { quantity: roundToFraction(grams / 453.592, 4) || 0.25, unit: 'lb' };
  }
  return { quantity: roundToFraction(grams / 28.3495, 2) || 0.5, unit: 'oz' };
}

/** Convert ml to the best recipe-friendly volume display (tsp, tbsp, cup) */
function toVolumeDisplay(ml: number): ShoppingPortion {
  if (ml >= 236.588) {
    return { quantity: roundToFraction(ml / 236.588, 4) || 0.25, unit: 'cup' };
  }
  if (ml >= 14.787) {
    return { quantity: roundToFraction(ml / 14.787, 4) || 0.25, unit: 'tbsp' };
  }
  return { quantity: roundToFraction(ml / 4.929, 4) || 0.25, unit: 'tsp' };
}

/** Convert ml to the best liquid display (fl oz or qt or gal) */
function toLiquidDisplay(ml: number): ShoppingPortion {
  const flOz = ml / 29.574;
  if (flOz >= 128) return { quantity: roundToFraction(flOz / 128, 4) || 0.25, unit: 'gal' };
  if (flOz >= 32) return { quantity: roundToFraction(flOz / 32, 4) || 0.25, unit: 'qt' };
  return { quantity: roundToFraction(flOz, 2) || 0.5, unit: 'fl oz' };
}

/**
 * Find the best retail packaging match for an ingredient name.
 * Matches by longest pattern (most specific). No sizeCategory filter —
 * the caller converts recipe units to the package's category via density.
 */
function findRetailPackage(
  name: string,
  packages: RetailPackagingResponse[]
): RetailPackagingResponse | null {
  const lower = name.toLowerCase();
  let bestMatch: RetailPackagingResponse | null = null;
  let bestLen = 0;

  for (const pkg of packages) {
    const pattern = pkg.ingredientPattern.toLowerCase();
    if (lower.includes(pattern) && pattern.length > bestLen) {
      bestMatch = pkg;
      bestLen = pattern.length;
    }
  }
  return bestMatch;
}

/** Pluralize a package name: "can" → "cans", "box" → "boxes", etc. */
function pluralizePackage(name: string, count: number): string {
  if (count <= 1) return name;
  const n = name.toLowerCase();
  if (n === 'box') return 'boxes';
  if (n === 'bunch') return 'bunches';
  if (n === 'loaf') return 'loaves';
  if (n.endsWith('ch') || n.endsWith('sh') || n.endsWith('ss') || n.endsWith('x') || n.endsWith('z'))
    return name + 'es';
  return name + 's';
}

/**
 * Format a retail packaging portion for display.
 * 1 package  → "16.9 fl oz bottle"   (omit count of 1)
 * 2 packages → "2 × 8 oz boxes"      (× separator prevents number collision)
 */
function formatRetailPortion(pkg: RetailPackagingResponse, pkgCount: number): ShoppingPortion {
  const pkgLabel = `${pkg.packageSize} ${pkg.packageSizeUnit} ${pluralizePackage(pkg.packageName, pkgCount)}`;
  if (pkgCount <= 1) {
    return { quantity: 0, unit: pkgLabel };
  }
  return { quantity: 0, unit: `${pkgCount} × ${pkgLabel}` };
}

/** Is this a small-volume item (spice, seasoning, extract) that should stay in tsp/tbsp? */
function isSmallVolumeItem(name: string): boolean {
  return /\b(salt|pepper|paprika|cumin|cinnamon|turmeric|oregano|chili powder|cayenne|nutmeg|coriander|garlic powder|onion powder|ginger powder|allspice|cardamom|cloves|fennel seed|mustard powder|saffron|baking soda|baking powder|cream of tartar|vanilla extract|vanilla|almond extract|extract|seasoning|spice)\b/i.test(name);
}

/** Convert ml to the best small-volume display (tsp or tbsp) */
function toSmallVolumeDisplay(ml: number): ShoppingPortion {
  const tbsp = ml / 14.787;
  if (tbsp >= 1) {
    return { quantity: roundToFraction(tbsp, 4) || 0.25, unit: 'tbsp' };
  }
  const tsp = ml / 4.929;
  return { quantity: roundToFraction(tsp, 4) || 0.25, unit: 'tsp' };
}

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
    MatTooltipModule,
  ],
  templateUrl: './shopping.component.html',
  styleUrl: './shopping.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShoppingComponent implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);
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
      ShoppingComponent.addDays(new Date(), this.daysAhead())
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
      const isCountable = /\b(egg|tortilla|wrap|pita|naan|bun|roll|bagel|muffin|slice|sheet|leaf)\b/i.test(name);
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
          totalBase = totalMassG
            + totalVolumeMl * density
            + (totalCount > 0 && !isCountable ? totalCount * 100 : 0);
        } else if (pkg.sizeCategory === 'volume') {
          // Convert everything to ml to match volume-based package
          totalBase = totalVolumeMl
            + (density > 0 ? totalMassG / density : 0)
            + (totalCount > 0 && !isCountable ? totalCount * 236.588 : 0);
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
        baseMassG: accs.filter(a => a.category === 'mass').reduce((s, a) => s + a.baseQuantity, 0),
        baseVolumeMl: accs.filter(a => a.category === 'volume').reduce((s, a) => s + a.baseQuantity, 0),
        baseCount: accs.filter(a => a.category === 'count' || a.category === 'other').reduce((s, a) => s + a.baseQuantity, 0),
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
    this.departments().reduce((sum, dept) => sum + dept.items.length, 0)
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
      a.download = `shopping-list-${new Date().toISOString().split('T')[0]}.csv`;
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
    return item.portions.map(p => {
      const qty = p.quantity > 0 ? this.formatQuantity(p.quantity) + ' ' : '';
      return qty + p.unit;
    }).join(' + ');
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
    const gramId = allMeasurements.find(m => m.name.toLowerCase() === 'gram')?.id;
    const mlId = allMeasurements.find(m => m.name.toLowerCase() === 'milliliter')?.id;
    const pieceId = allMeasurements.find(m => m.name.toLowerCase() === 'piece')?.id;

    if (!gramId || !mlId || !pieceId) {
      this.snackBar.open('Measurement data not loaded. Please refresh and try again.', 'OK', { duration: 4000 });
      return;
    }

    const today = new Date();
    const todayStr = today.toISOString().split('T')[0];
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
          measurementId = parsed.measurementId ?? this.pickBestMeasurement(item, gramId, mlId, pieceId);
        } else if (item.retailPackage && item.retailPackageCount > 0) {
          // Use retail package: pkgCount × package base units
          const totalBase = item.retailPackageCount * item.retailPackage.sizeInBaseUnits;
          if (item.retailPackage.sizeCategory === 'mass') {
            quantity = totalBase; measurementId = gramId;
          } else if (item.retailPackage.sizeCategory === 'volume') {
            quantity = totalBase; measurementId = mlId;
          } else {
            quantity = totalBase; measurementId = pieceId;
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
          expectedExpirationDate: expDate.toISOString().split('T')[0],
        });
      }
    }

    if (items.length === 0) {
      this.snackBar.open('No valid items to transfer.', 'OK', { duration: 3000 });
      return;
    }

    this.completingTrip.set(true);
    this.pantryService.addPantryItemsBatch(items).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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

  private parseOverride(text: string, item: ShoppingItem): { quantity: number; measurementId: number | null } {
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
        const gramId = allMeasurements.find(m => m.name.toLowerCase() === 'gram')?.id;
        const mlId = allMeasurements.find(m => m.name.toLowerCase() === 'milliliter')?.id;
        const pieceId = allMeasurements.find(m => m.name.toLowerCase() === 'piece')?.id;
        if (item.retailPackage.sizeCategory === 'mass') return { quantity: totalBase, measurementId: gramId ?? null };
        if (item.retailPackage.sizeCategory === 'volume') return { quantity: totalBase, measurementId: mlId ?? null };
        return { quantity: totalBase, measurementId: pieceId ?? null };
      }
      return { quantity: num, measurementId: null };
    }

    // Try to match unit text to known measurements
    const unitMap: Record<string, string> = {
      'g': 'gram', 'gram': 'gram', 'grams': 'gram',
      'oz': 'ounce', 'ounce': 'ounce', 'ounces': 'ounce',
      'lb': 'pound', 'lbs': 'pound', 'pound': 'pound', 'pounds': 'pound',
      'kg': 'kilogram', 'kilogram': 'kilogram', 'kilograms': 'kilogram',
      'ml': 'milliliter', 'milliliter': 'milliliter', 'milliliters': 'milliliter',
      'l': 'liter', 'liter': 'liter', 'liters': 'liter',
      'cup': 'cup', 'cups': 'cup',
      'tbsp': 'tablespoon', 'tablespoon': 'tablespoon', 'tablespoons': 'tablespoon',
      'tsp': 'teaspoon', 'teaspoon': 'teaspoon', 'teaspoons': 'teaspoon',
      'piece': 'piece', 'pieces': 'piece', 'each': 'piece',
      'dozen': 'dozen',
    };

    const mappedName = unitMap[unitText];
    if (mappedName) {
      const meas = allMeasurements.find(m => m.name.toLowerCase() === mappedName);
      if (meas) {
        // Convert to base units for pantry storage
        const info = getUnitInfo(mappedName);
        return { quantity: num * info.toBase, measurementId: allMeasurements.find(m => m.name.toLowerCase() === (info.category === 'mass' ? 'gram' : info.category === 'volume' ? 'milliliter' : 'piece'))?.id ?? null };
      }
    }

    // If unit is a package name (can, box, bottle, bag, jar, tub, bunch, etc.)
    if (/^(cans?|boxes?|bottles?|bags?|jars?|tubs?|bunch|bunches|cartons?|packs?|containers?)$/.test(unitText)) {
      // Use the number × retail package size if available
      if (item.retailPackage) {
        const totalBase = num * item.retailPackage.sizeInBaseUnits;
        const gramId = allMeasurements.find(m => m.name.toLowerCase() === 'gram')?.id;
        const mlId = allMeasurements.find(m => m.name.toLowerCase() === 'milliliter')?.id;
        const pieceId = allMeasurements.find(m => m.name.toLowerCase() === 'piece')?.id;
        if (item.retailPackage.sizeCategory === 'mass') return { quantity: totalBase, measurementId: gramId ?? null };
        if (item.retailPackage.sizeCategory === 'volume') return { quantity: totalBase, measurementId: mlId ?? null };
        return { quantity: totalBase, measurementId: pieceId ?? null };
      }
    }

    // Fallback: just use the number
    return { quantity: num, measurementId: null };
  }

  private pickBestMeasurement(item: ShoppingItem, gramId: number, mlId: number, pieceId: number): number {
    if (item.baseMassG > 0) return gramId;
    if (item.baseVolumeMl > 0) return mlId;
    return pieceId;
  }

  ngOnInit(): void {
    this.householdService.getHouseholds().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.householdId.set(list[0].id);
          this.loadData();
        } else {
          this.loading.set(false);
          this.error.set('No household found.');
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
    if (qty === Math.floor(qty)) return qty.toString();
    const frac = qty - Math.floor(qty);
    const whole = Math.floor(qty);
    if (Math.abs(frac - 0.25) < 0.01) return whole ? `${whole} 1/4` : '1/4';
    if (Math.abs(frac - 0.33) < 0.02) return whole ? `${whole} 1/3` : '1/3';
    if (Math.abs(frac - 0.5) < 0.01) return whole ? `${whole} 1/2` : '1/2';
    if (Math.abs(frac - 0.67) < 0.02) return whole ? `${whole} 2/3` : '2/3';
    if (Math.abs(frac - 0.75) < 0.01) return whole ? `${whole} 3/4` : '3/4';
    return qty.toFixed(1);
  }

  private loadData(): void {
    this.error.set('');
    const today = new Date();
    const endDate = ShoppingComponent.addDays(today, this.daysAhead());
    const monday1 = ShoppingComponent.getMonday(today);
    const monday2 = ShoppingComponent.getMonday(endDate);

    const weekFetches = [
      this.mealPlanService.getWeek(this.householdId(), monday1),
    ];
    if (monday2 !== monday1) {
      weekFetches.push(
        this.mealPlanService.getWeek(this.householdId(), monday2)
      );
    }

    // Fetch meal plan weeks, pantry items, retail packaging, and measurements in parallel
    forkJoin({
      weeks: forkJoin(weekFetches),
      pantry: this.pantryService.getPantryItems(this.householdId()),
      packaging: this.retailPackagingService.getAll(),
      measurements: this.measurements().length > 0
        ? of(this.measurements())
        : this.measurementService.loadMeasurements(),
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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
      ShoppingComponent.addDays(new Date(), this.daysAhead())
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

    forkJoin(
      Array.from(recipeIds).map(id => this.recipeService.getRecipe(id))
    ).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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
    this.retailPackagingService.lookup(unmatchedNames).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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
      localStorage.setItem(
        this.storageKey,
        JSON.stringify([...this.checkedItems()])
      );
    } catch { /* localStorage unavailable */ }
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
    return date.toISOString().split('T')[0];
  }
}

/** Categorize an ingredient name into a grocery department */
function categorizeDepartment(name: string): string {
  const n = name.toLowerCase();

  // Produce
  if (/\b(lettuce|spinach|kale|arugula|cabbage|bok choy|collard|chard)\b/.test(n)) return 'Produce';
  if (/\b(tomato|onion|garlic|pepper|carrot|celery|potato|sweet potato)\b/.test(n)) return 'Produce';
  if (/\b(broccoli|cauliflower|zucchini|squash|eggplant|mushroom|corn)\b/.test(n)) return 'Produce';
  if (/\b(cucumber|avocado|bean sprout|scallion|green onion|shallot|leek)\b/.test(n)) return 'Produce';
  if (/\b(apple|banana|orange|lemon|lime|berry|berries|blueberr|strawberr|raspberr)\b/.test(n)) return 'Produce';
  if (/\b(grape|mango|pineapple|peach|pear|melon|watermelon|cherry|plum|kiwi)\b/.test(n)) return 'Produce';
  if (/\b(ginger|cilantro|parsley|basil|mint|dill|rosemary|thyme|chives|jalape)\b/.test(n)) return 'Produce';
  if (/\b(asparagus|artichoke|beet|radish|turnip|parsnip|fennel|okra|peas)\b/.test(n)) return 'Produce';
  if (/\b(green bean|snap pea|snow pea|edamame|brussels sprout|watercress)\b/.test(n)) return 'Produce';

  // Meat & Seafood
  if (/\b(chicken|turkey|beef|pork|lamb|veal|duck|bison|venison)\b/.test(n)) return 'Meat & Seafood';
  if (/\b(steak|ground meat|ground beef|ground turkey|ground pork|sausage|bacon|ham|prosciutto)\b/.test(n)) return 'Meat & Seafood';
  if (/\b(salmon|tuna|shrimp|prawn|cod|tilapia|halibut|trout|crab|lobster|scallop|clam|mussel|anchov)\b/.test(n)) return 'Meat & Seafood';
  if (/\b(fish sauce)\b/.test(n)) return 'Condiments & Sauces';
  if (/\b(fish)\b/.test(n)) return 'Meat & Seafood';

  // Dairy & Eggs
  if (/\b(milk|cream|half.and.half|buttermilk|yogurt|kefir|sour cream|cr[eè]me)\b/.test(n)) return 'Dairy & Eggs';
  if (/\b(cheese|parmesan|mozzarella|cheddar|feta|ricotta|gouda|brie|gruy[eè]re|cream cheese)\b/.test(n)) return 'Dairy & Eggs';
  if (/\b(butter|margarine|ghee)\b/.test(n)) return 'Dairy & Eggs';
  if (/\b(egg)\b/.test(n)) return 'Dairy & Eggs';

  // Bakery
  if (/\b(bread|baguette|roll|bun|pita|naan|tortilla|wrap|croissant|english muffin|bagel)\b/.test(n)) return 'Bakery';

  // Grains & Pasta
  if (/\b(rice|pasta|spaghetti|penne|linguine|fettuccine|macaroni|noodle|ramen|udon|soba)\b/.test(n)) return 'Grains & Pasta';
  if (/\b(quinoa|couscous|barley|farro|bulgur|polenta|oat|oatmeal|cereal|granola)\b/.test(n)) return 'Grains & Pasta';
  if (/\b(lentil|chickpea|black bean|kidney bean|pinto bean|cannellini|navy bean)\b/.test(n)) return 'Grains & Pasta';

  // Canned & Jarred
  if (/\b(canned|diced tomato|crushed tomato|tomato paste|tomato sauce|salsa|marinara)\b/.test(n)) return 'Canned & Jarred';
  if (/\b(broth|stock|bouillon|coconut milk|coconut cream)\b/.test(n)) return 'Canned & Jarred';
  if (/\b(peanut butter|almond butter|jam|jelly|preserve|nutella)\b/.test(n)) return 'Canned & Jarred';

  // Condiments & Sauces
  if (/\b(soy sauce|tamari|worcestershire|hot sauce|sriracha|tabasco)\b/.test(n)) return 'Condiments & Sauces';
  if (/\b(ketchup|mustard|mayo|mayonnaise|relish|barbecue|teriyaki|hoisin|oyster sauce)\b/.test(n)) return 'Condiments & Sauces';
  if (/\b(vinegar|dressing|marinade|miso|tahini|harissa|gochujang|sambal)\b/.test(n)) return 'Condiments & Sauces';
  if (/\b(honey|maple syrup|agave|molasses)\b/.test(n)) return 'Condiments & Sauces';

  // Spices & Seasonings
  if (/\b(salt|pepper|cumin|paprika|chili powder|cayenne|turmeric|cinnamon|nutmeg)\b/.test(n)) return 'Spices & Seasonings';
  if (/\b(oregano|thyme|rosemary|sage|bay leaf|coriander|cardamom|clove|allspice)\b/.test(n)) return 'Spices & Seasonings';
  if (/\b(garlic powder|onion powder|smoked paprika|red pepper flake|italian season|curry powder)\b/.test(n)) return 'Spices & Seasonings';
  if (/\b(sesame seed|poppy seed|fennel seed|mustard seed|caraway|star anise|saffron)\b/.test(n)) return 'Spices & Seasonings';

  // Oils & Vinegars
  if (/\b(olive oil|vegetable oil|canola oil|coconut oil|sesame oil|avocado oil|cooking spray)\b/.test(n)) return 'Oils & Vinegars';
  if (/\b(balsamic|rice vinegar|apple cider vinegar|red wine vinegar|white wine vinegar)\b/.test(n)) return 'Oils & Vinegars';

  // Baking
  if (/\b(flour|sugar|brown sugar|powdered sugar|baking soda|baking powder|yeast)\b/.test(n)) return 'Baking';
  if (/\b(vanilla|cocoa|chocolate chip|cornstarch|corn starch|gelatin|food color)\b/.test(n)) return 'Baking';
  if (/\b(almond flour|coconut flour|breadcrumb|panko)\b/.test(n)) return 'Baking';

  // Frozen
  if (/\b(frozen|ice cream)\b/.test(n)) return 'Frozen';

  // Beverages
  if (/\b(juice|coffee|tea|water|soda|wine|beer|sparkling)\b/.test(n)) return 'Beverages';

  // Nuts & Seeds (put in Other or a specific dept)
  if (/\b(almond|walnut|pecan|cashew|pistachio|peanut|hazelnut|macadamia|pine nut|sunflower seed|pumpkin seed|chia|flax)\b/.test(n)) return 'Other';
  if (/\b(tofu|tempeh|seitan)\b/.test(n)) return 'Other';

  return 'Other';
}
