import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MealPlanService } from '../core/services/meal-plan.service';
import { HouseholdService } from '../core/services/household.service';
import { RecipeService } from '../core/services/recipe.service';
import { PantryService } from '../core/services/pantry.service';
import { RetailPackagingService } from '../core/services/retail-packaging.service';
import { MealPlanWeekResponse } from '../core/models/meal-plan.model';
import { RecipeModel } from '../core/models/recipe.model';
import { PantryItemResponse } from '../core/models/pantry.model';
import { RetailPackagingResponse } from '../core/models/retail-packaging.model';

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

/** Convert ml to the best liquid display (fl oz or qt or gal) */
function toLiquidDisplay(ml: number): ShoppingPortion {
  const flOz = ml / 29.574;
  if (flOz >= 128) return { quantity: roundToFraction(flOz / 128, 4) || 0.25, unit: 'gal' };
  if (flOz >= 32) return { quantity: roundToFraction(flOz / 32, 4) || 0.25, unit: 'qt' };
  return { quantity: roundToFraction(flOz, 2) || 0.5, unit: 'fl oz' };
}

/**
 * Find the best retail packaging match for an ingredient name.
 * Prefers exact matches, then longest partial match (more specific wins).
 */
function findRetailPackage(
  name: string,
  sizeCategory: 'volume' | 'mass' | 'count',
  packages: RetailPackagingResponse[]
): RetailPackagingResponse | null {
  const lower = name.toLowerCase();
  let bestMatch: RetailPackagingResponse | null = null;
  let bestLen = 0;

  for (const pkg of packages) {
    if (pkg.sizeCategory !== sizeCategory) continue;
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
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './shopping.component.html',
  styleUrl: './shopping.component.scss',
})
export class ShoppingComponent implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);
  private recipeService = inject(RecipeService);
  private pantryService = inject(PantryService);
  private retailPackagingService = inject(RetailPackagingService);

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
    const todayDate = new Date().toISOString().split('T')[0];
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

      // Try retail packaging first — convert to "X cans", "1 box", etc.
      let handledByRetail = false;

      // For mass-based ingredients, try matching a mass retail package
      if (totalMassG > 0) {
        const pkg = findRetailPackage(name, 'mass', packages);
        if (pkg) {
          // Also fold in volume via density
          if (totalVolumeMl > 0) {
            totalMassG += totalVolumeMl * getIngredientDensity(name);
            totalVolumeMl = 0;
          }
          const pkgCount = Math.ceil(totalMassG / pkg.sizeInBaseUnits);
          portions.push({
            quantity: pkgCount,
            unit: `${pkg.packageSize} ${pkg.packageSizeUnit} ${pluralizePackage(pkg.packageName, pkgCount)}`,
          });
          handledByRetail = true;
          totalMassG = 0;
        }
      }

      // For volume-based ingredients, try matching a volume retail package
      if (totalVolumeMl > 0) {
        const pkg = findRetailPackage(name, 'volume', packages);
        if (pkg) {
          const pkgCount = Math.ceil(totalVolumeMl / pkg.sizeInBaseUnits);
          portions.push({
            quantity: pkgCount,
            unit: `${pkg.packageSize} ${pkg.packageSizeUnit} ${pluralizePackage(pkg.packageName, pkgCount)}`,
          });
          handledByRetail = true;
          totalVolumeMl = 0;
        }
      }

      // For count-based ingredients, try matching a count retail package
      if (totalCount > 0) {
        const pkg = findRetailPackage(name, 'count', packages);
        if (pkg) {
          const pkgCount = Math.ceil(totalCount / pkg.sizeInBaseUnits);
          portions.push({
            quantity: pkgCount,
            unit: `${pkg.packageSize} ${pkg.packageSizeUnit} ${pluralizePackage(pkg.packageName, pkgCount)}`,
          });
          handledByRetail = true;
          totalCount = 0;
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
        } else if (isLiquid(name)) {
          let totalMl = totalVolumeMl;
          if (totalMassG > 0) totalMl += totalMassG;
          if (totalMl > 0) {
            portions.push(toLiquidDisplay(totalMl));
          }
        } else {
          let totalG = totalMassG;
          if (totalVolumeMl > 0) {
            totalG += totalVolumeMl * getIngredientDensity(name);
          }
          if (totalG > 0) {
            portions.push(toWeightDisplay(totalG));
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

  ngOnInit(): void {
    this.householdService.getHouseholds().subscribe({
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

    // Fetch meal plan weeks, pantry items, and retail packaging in parallel
    forkJoin({
      weeks: forkJoin(weekFetches),
      pantry: this.pantryService.getPantryItems(this.householdId()),
      packaging: this.retailPackagingService.getAll(),
    }).subscribe({
      next: ({ weeks, pantry, packaging }) => {
        this.weekDataList.set(weeks);
        this.pantryItems.set(pantry);
        this.retailPackages.set(packaging);
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
        const hasMatch =
          findRetailPackage(item.name, 'mass', packages) ||
          findRetailPackage(item.name, 'volume', packages) ||
          findRetailPackage(item.name, 'count', packages);
        if (!hasMatch) {
          unmatchedNames.push(item.name);
        }
      }
    }

    if (unmatchedNames.length === 0) return;

    this.lookingUpPackaging.set(true);
    this.retailPackagingService.lookup(unmatchedNames).subscribe({
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
