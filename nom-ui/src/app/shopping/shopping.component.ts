import {
  Component,
  inject,
  signal,
  computed,
  OnInit,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { EntityLink } from '../shared/components/entity-link/entity-link.component';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { NoHouseholdCta } from '../shared/components/no-household-cta/no-household-cta.component';
import { toLocalDateString } from '../core/utils/local-date';
import { downloadTextFile, shoppingListFileName } from '../core/utils/download-file';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
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
import { PortionService } from '../core/services/portion.service';
import { GroceryExportService } from '../core/services/grocery-export.service';
import { HouseholdShoppingService } from '../core/services/household-shopping.service';
import { HouseholdShoppingItem, ShoppingState } from '../core/models/household-shopping.model';
import { ShoppingAddItem } from './shopping-add-item.component';
import { VerificationFlowService } from './verification/verification-flow.service';
import { adhocCheckKey, mergeAdhocItems, parseAdhocQuantity } from '../core/domain/shopping/adhoc-items';
import { GroceryExportItem, GroceryProviderInfo } from '../core/models/grocery-export.model';
import {
  GroceryExportDialog,
  GroceryExportDialogData,
} from './grocery-export-dialog.component';
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
  inPackage,
} from '../core/domain/shopping/departments';
import { shoppingListShareData, shoppingListText } from '../core/domain/shopping/list-text';
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
  pluralizePackage,
} from '../core/domain/shopping/unit-conversion';

/** Accumulator used during ingredient merging */
interface RawAccumulator {
  ingredientId: number;
  name: string;
  baseQuantity: number;
  category: UnitCategory;
  originalUnit: string;
  department: string;
  /** Planned recipes contributing to this line, keyed by recipe id. */
  sourceRecipes: Map<number, string>;
}

@Component({
  selector: 'nom-shopping',
  imports: [
    RouterLink,
    EntityLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule, ErrorBanner, NoHouseholdCta, ShoppingAddItem],
  providers: [VerificationFlowService],
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
  private portionService = inject(PortionService);
  private groceryExportService = inject(GroceryExportService);
  private householdShopping = inject(HouseholdShoppingService);
  private verificationFlow = inject(VerificationFlowService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  /** Per-planned-recipe cook factors keyed `date|mealTypeId|recipeId` (portion scaling). */
  private cookFactors = signal<Map<string, number>>(new Map());

  /** True when any planned recipe is scaled away from 1× (note shown in header). */
  scalingActive = computed(() => {
    for (const f of this.cookFactors().values()) {
      if (Math.abs(f - 1) > 0.05) return true;
    }
    return false;
  });
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
  /** Items household members typed in, held on the server. */
  adhocItems = signal<HouseholdShoppingItem[]>([]);
  /** Foods the server says to ask about on the next tick; an id leaves the set the moment it is asked. */
  askable = signal<Set<number>>(new Set());
  private askedThisSession = new Set<number>();

  // Inline quantity editing
  editingItem = signal<string | null>(null);
  quantityOverrides = signal<Map<string, string>>(new Map());

  // Complete trip state
  completingTrip = signal(false);
  measurements = signal<{ id: number; name: string; symbol: string }[]>([]);

  private plannedDepartments = computed<ShoppingDepartment[]>(() => {
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

            // Scale to household portions when a cook factor exists for this
            // planned recipe (macro goals + meal split); 1× otherwise.
            const cookFactor =
              this.cookFactors().get(`${day.date}|${cell.mealTypeId}|${entry.recipeId}`) ?? 1;

            for (const ing of recipe.ingredients) {
              const info = getUnitInfo(ing.measurement ?? '');
              const key = `${ing.ingredientId}-${info.category}`;
              const baseQty = ing.quantity * info.toBase * cookFactor;

              const existing = accMap.get(key);
              if (existing) {
                existing.baseQuantity += baseQty;
                existing.sourceRecipes.set(entry.recipeId, recipe.name);
              } else {
                accMap.set(key, {
                  ingredientId: ing.ingredientId,
                  name: ing.name,
                  baseQuantity: baseQty,
                  category: info.category,
                  originalUnit: ing.measurement ?? '',
                  department: categorizeDepartment(ing.name, ing.foodGroupName),
                  sourceRecipes: new Map([[entry.recipeId, recipe.name]]),
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
      let dept = accs[0].department;
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
        dept = inPackage(dept, pkg.packageName);
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

      const sourceById = new Map<number, string>();
      for (const a of accs) {
        for (const [id, recipeName] of a.sourceRecipes) sourceById.set(id, recipeName);
      }

      const item: ShoppingItem = {
        ingredientId,
        name,
        portions,
        department: dept,
        checkKey: `${ingredientId}`,
        sourceRecipes: Array.from(sourceById, ([id, recipeName]) => ({ id, name: recipeName }))
          .sort((a, b) => a.name.localeCompare(b.name)),
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
        source: 'plan',
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

  departments = computed<ShoppingDepartment[]>(() =>
    mergeAdhocItems(this.plannedDepartments(), this.adhocItems()),
  );

  totalItemCount = computed(() =>
    this.departments().reduce((sum, dept) => sum + dept.items.length, 0),
  );

  checkedCount = computed(() => {
    const checked = this.checkedItems();
    let count = 0;
    for (const dept of this.departments()) {
      for (const item of dept.items) if (checked.has(item.checkKey)) count++;
    }
    return count;
  });

  /** Foods already on the list, so the add field can refuse a duplicate before posting. */
  existingIngredientIds = computed<ReadonlySet<number>>(() => {
    const ids = new Set<number>();
    for (const dept of this.departments()) {
      for (const item of dept.items) if (item.ingredientId !== null) ids.add(item.ingredientId);
    }
    return ids;
  });

  // --- Grocery export ("Send to…") ---

  /**
   * Destinations the operator's grocery service advertises. An empty array is
   * the normal, expected state on a server with no grocery integration — the
   * whole feature stays out of the DOM in that case.
   */
  providers = signal<GroceryProviderInfo[]>([]);

  private connectResult = signal<{ ok: boolean; provider: string | null } | null>(null);

  /** Notice shown after returning from a retailer's consent screen. */
  connectNotice = computed<{ ok: boolean; message: string } | null>(() => {
    const result = this.connectResult();
    if (!result) return null;

    const advertised = this.providers().find(
      (p) => p.key.toLowerCase() === result.provider?.toLowerCase(),
    );
    const key = result.provider?.trim();
    const name =
      advertised?.displayName ||
      (key ? key.charAt(0).toUpperCase() + key.slice(1) : null);

    if (result.ok) {
      return {
        ok: true,
        message: `${name ?? 'Your grocery account'} is connected — you can send lists to it now.`,
      };
    }

    const retry = advertised?.configured ? ' Please try again.' : '';
    return {
      ok: false,
      message: `${name ?? 'That grocery account'} could not be connected.${retry}`,
    };
  });

  /** The stretch of meals being shopped, used as the export title. */
  exportTitle = computed(() => {
    const today = new Date();
    const last = ShoppingComponent.addDays(today, this.daysAhead() - 1);
    const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
    return `Shopping List — ${today.toLocaleDateString(undefined, opts)} – ${last.toLocaleDateString(undefined, opts)}`;
  });

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
    const item = this.findItem(checkKey);
    if (item?.adhocId !== undefined) {
      this.saveAdhocQuantity(item.adhocId, trimmed);
    } else if (trimmed) {
      const overrides = new Map(this.quantityOverrides());
      overrides.set(checkKey, trimmed);
      this.quantityOverrides.set(overrides);
    }
    this.editingItem.set(null);
  }

  /** An amount typed on an added line belongs to the household's row, so every phone sees it. */
  private saveAdhocQuantity(id: number, text: string): void {
    const current = this.adhocItems().find((i) => i.id === id);
    if (!current) return;
    const { quantity, unit } = parseAdhocQuantity(text);
    if ((current.quantity ?? null) === quantity && (current.unit ?? null) === unit) return;
    const key = adhocCheckKey(id);
    const overrides = new Map(this.quantityOverrides());
    if (text) overrides.set(key, text);
    else overrides.delete(key);
    this.quantityOverrides.set(overrides);
    this.householdShopping
      .updateItem(id, { quantity, unit, ingredientId: current.ingredientId ?? null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.adhocItems.update((list) => list.map((i) => (i.id === id ? saved : i)));
          this.quantityOverrides.update((map) => {
            const next = new Map(map);
            next.delete(key);
            return next;
          });
        },
        error: () => {
          this.quantityOverrides.update((map) => {
            const next = new Map(map);
            next.delete(key);
            return next;
          });
          this.snackBar.open(`Could not save the amount for ${current.name} — check your connection`, 'OK', { duration: 4000 });
        },
      });
  }

  cancelEdit(): void {
    this.editingItem.set(null);
  }

  // --- Share / Export ---

  exportList(format: 'text' | 'csv'): void {
    const items = this.buildExportItems(true);
    if (items.length === 0) {
      this.snackBar.open('Every item is checked off — nothing left to export', 'OK', { duration: 3000 });
      return;
    }

    if (format === 'csv') {
      downloadTextFile(shoppingListFileName('csv'), this.buildListCsv(items), 'text/csv');
      this.snackBar.open('CSV downloaded', 'OK', { duration: 2000 });
    } else {
      this.copyText(this.buildListText(items));
    }
  }

  shareList(): void {
    const items = this.buildExportItems(true);
    if (items.length === 0) {
      this.snackBar.open('Every item is checked off — nothing left to share', 'OK', { duration: 3000 });
      return;
    }

    const data = shoppingListShareData(this.exportTitle(), items, (item) => this.exportLine(item));
    const text = data.text ?? '';
    if (navigator.share) {
      navigator.share(data).catch((err: unknown) => {
        if ((err as DOMException)?.name !== 'AbortError') this.copyText(text);
      });
    } else {
      this.copyText(text);
    }
  }

  private buildListText(items: GroceryExportItem[]): string {
    return shoppingListText(this.exportTitle(), items, (item) => this.exportLine(item));
  }

  /** Same columns as the grocery service's CSV, so both downloads open alike. */
  private buildListCsv(items: GroceryExportItem[]): string {
    const cell = (value: string | undefined) =>
      value && /[",\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : (value ?? '');
    const lines = ['Category,Item,Quantity,Unit,Package,Note'];
    for (const item of items) {
      lines.push(
        [
          item.category,
          item.name,
          item.quantity !== undefined ? String(Math.round(item.quantity * 100) / 100) : undefined,
          item.unit,
          item.packageHint,
          item.note,
        ]
          .map(cell)
          .join(','),
      );
    }
    return lines.join('\n') + '\n';
  }

  /** "2 1/2 oz Asparagus", "1 block Cheddar Cheese (8 oz block)", "Bread — 1 loaf + 2". */
  private exportLine(item: GroceryExportItem): string {
    const amount =
      item.quantity !== undefined
        ? [this.formatQuantity(item.quantity), item.unit].filter(Boolean).join(' ') + ' '
        : '';
    let line = amount + item.name;
    if (item.packageHint) line += ` (${item.packageHint})`;
    if (item.note) line += ` — ${item.note}`;
    return line;
  }

  private copyText(text: string): void {
    navigator.clipboard.writeText(text).then(
      () => this.snackBar.open('Copied to clipboard', 'OK', { duration: 2000 }),
      () => this.snackBar.open('Could not copy to the clipboard', 'OK', { duration: 3000 }),
    );
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

  // --- Grocery export ("Send to…") ---

  /**
   * An unconfigured server answers with an empty list, and a failure is treated
   * the same way: no destinations means no button, no menu, nothing rendered.
   */
  private loadGroceryProviders(): void {
    this.groceryExportService
      .getProviders()
      .pipe(
        catchError(() => of([] as GroceryProviderInfo[])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((providers) => this.providers.set(providers));
  }

  openSendTo(): void {
    const providers = this.providers();
    if (providers.length === 0) return;

    this.dialog.open(GroceryExportDialog, {
      data: {
        providers,
        // Evaluated when the user sends, so the toggle sees current check state.
        buildItems: (excludeChecked: boolean) => this.buildExportItems(excludeChecked),
        title: this.exportTitle(),
        // Where the retailer consent flow lands the browser again.
        returnUrl: this.router.url.split('?')[0],
      } satisfies GroceryExportDialogData,
      autoFocus: false,
    });
  }

  /**
   * Flatten the rendered projection into export lines. An item shown as one
   * computed amount exports it as `quantity`/`unit` (a retail package as its
   * count of packages); anything else the projection can only show as text —
   * several portions, or the user's inline override — rides along in `note`
   * and `quantity`/`unit` stay unset rather than being guessed at.
   */
  private buildExportItems(excludeChecked: boolean): GroceryExportItem[] {
    const checked = this.checkedItems();
    const items: GroceryExportItem[] = [];

    for (const dept of this.departments()) {
      for (const item of dept.items) {
        if (excludeChecked && checked.has(item.checkKey)) continue;

        const amount = this.exportAmount(item);
        const note = amount ? '' : this.getDisplayText(item).trim();
        items.push({
          name: item.name,
          ...amount,
          category: dept.name,
          packageHint: this.packageHint(item),
          note: note || undefined,
        });
      }
    }

    return items;
  }

  private exportAmount(item: ShoppingItem): { quantity: number; unit?: string } | null {
    if (this.quantityOverrides().has(item.checkKey) || item.portions.length !== 1) return null;

    const portion = item.portions[0];
    if (portion.quantity > 0) return { quantity: portion.quantity, unit: portion.unit || undefined };

    const pkg = item.retailPackage;
    if (!pkg || item.retailPackageCount <= 0) return null;
    return {
      quantity: item.retailPackageCount,
      unit: pluralizePackage(pkg.packageName, item.retailPackageCount),
    };
  }

  /** e.g. "2 × 5 lb bag" — helps the retailer land on a sane package size. */
  private packageHint(item: ShoppingItem): string | undefined {
    const pkg = item.retailPackage;
    if (!pkg) return undefined;

    const size = `${formatQuantity(pkg.packageSize)} ${pkg.packageSizeUnit} ${pkg.packageName}`.trim();
    return item.retailPackageCount > 1 ? `${item.retailPackageCount} × ${size}` : size;
  }

  /**
   * The API bounces the browser back here after a retailer consent screen with
   * `?connected=ok|failed&provider=…`. Report it once, then scrub the params so
   * a refresh doesn't replay the notice.
   */
  private readConnectResult(): void {
    const params = this.route.snapshot.queryParamMap;
    const connected = params.get('connected');
    if (connected !== 'ok' && connected !== 'failed') return;

    this.connectResult.set({ ok: connected === 'ok', provider: params.get('provider') });

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { connected: null, provider: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  dismissConnectNotice(): void {
    this.connectResult.set(null);
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
    const boughtAdhocIds: number[] = [];

    for (const dept of departments) {
      for (const item of dept.items) {
        if (!checked.has(item.checkKey)) continue;
        if (item.adhocId !== undefined) boughtAdhocIds.push(item.adhocId);
        if (item.ingredientId === null) continue;

        // Determine quantity and measurement based on override or original data
        const override =
          this.quantityOverrides().get(item.checkKey) ??
          (item.source === 'adhoc' && item.portions.length > 0 ? this.getDisplayText(item) : undefined);
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

    if (items.length === 0 && boughtAdhocIds.length === 0) {
      this.snackBar.open('No valid items to transfer.', 'OK', { duration: 3000 });
      return;
    }

    this.completingTrip.set(true);
    const transfer = items.length > 0 ? this.pantryService.addPantryItemsBatch(items) : of([]);
    transfer.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (items.length > 0) {
          this.snackBar.open(`${items.length} item(s) added to pantry!`, 'OK', { duration: 3000 });
        }
        this.finishTrip(boughtAdhocIds);
      },
      error: () => {
        this.snackBar.open('Failed to add items to pantry.', 'OK', { duration: 4000 });
        this.completingTrip.set(false);
      },
    });
  }

  /** Clears the household's shared ticks and drops the typed-in items that were bought, then reloads. */
  private finishTrip(boughtAdhocIds: number[]): void {
    forkJoin([
      this.householdShopping.clearChecks(this.householdId()),
      ...boughtAdhocIds.map((id) => this.householdShopping.removeItem(id)),
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.afterTrip(),
        error: () => {
          this.snackBar.open('Pantry updated, but the list could not be cleared for everyone.', 'OK', {
            duration: 4000,
          });
          this.afterTrip();
        },
      });
  }

  private afterTrip(): void {
    this.checkedItems.set(new Set());
    this.quantityOverrides.set(new Map());
    this.completingTrip.set(false);
    this.loading.set(true);
    this.loadData();
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
    this.readConnectResult();
    this.loadGroceryProviders();

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
    const next = !this.checkedItems().has(key);
    this.setCheckedLocally(key, next);

    const item = this.findItem(key);
    const body =
      item?.adhocId !== undefined
        ? { householdId: this.householdId(), itemId: item.adhocId, checked: next }
        : { householdId: this.householdId(), itemKey: key, checked: next };

    this.householdShopping
      .setChecked(body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (!next || !item || item.ingredientId === null) return;
          const verification = result?.verification;
          const serverSays = verification ? verification.shouldAsk : null;
          if (serverSays === true) this.ask(item.ingredientId, item);
          else if (serverSays === null && this.askable().has(item.ingredientId)) this.ask(item.ingredientId, item);
        },
        error: () => {
          this.setCheckedLocally(key, !next);
          this.snackBar
            .open('Could not save that tick — check your connection', 'Retry', { duration: 4000 })
            .onAction()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(() => this.toggleChecked(key));
        },
      });
  }

  private setCheckedLocally(key: string, checked: boolean): void {
    const set = new Set(this.checkedItems());
    if (checked) set.add(key);
    else set.delete(key);
    this.checkedItems.set(set);
  }

  private findItem(key: string): ShoppingItem | undefined {
    for (const dept of this.departments()) {
      const found = dept.items.find((i) => i.checkKey === key);
      if (found) return found;
    }
    return undefined;
  }

  /**
   * Opens the verification sheets once per food per session, after the tick has
   * been saved. The server's answer on the tick wins; the askable set only covers
   * a response that did not carry one.
   */
  private ask(ingredientId: number, item: ShoppingItem): void {
    if (this.askedThisSession.has(ingredientId)) return;
    this.askedThisSession.add(ingredientId);
    const remaining = new Set(this.askable());
    remaining.delete(ingredientId);
    this.askable.set(remaining);
    void this.verificationFlow
      .run({ ingredientId, name: item.name, adhoc: item.source === 'adhoc' }, this.householdId())
      .then((outcome) => {
        if (outcome === 'not-food') this.unlinkAdhocFood(ingredientId);
      });
  }

  private unlinkAdhocFood(ingredientId: number): void {
    this.adhocItems.update((list) =>
      list.map((i) => (i.ingredientId === ingredientId ? { ...i, ingredientId: null, ingredientNeedsVerification: false } : i)),
    );
  }

  onItemAdded(item: HouseholdShoppingItem): void {
    this.adhocItems.update((list) => [...list.filter((i) => i.id !== item.id), item]);
    this.setCheckedLocally(adhocCheckKey(item.id), item.checked);
    const ingredientId = item.ingredientId;
    if (ingredientId && item.ingredientNeedsVerification) {
      this.askable.update((set) => new Set(set).add(ingredientId));
    }
  }

  removeAdhoc(item: ShoppingItem): void {
    const id = item.adhocId;
    if (id === undefined) return;
    const before = this.adhocItems();
    this.adhocItems.set(before.filter((i) => i.id !== id));
    this.householdShopping
      .removeItem(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.adhocItems.set(before);
          this.snackBar.open(`Could not remove ${item.name} — check your connection`, 'OK', { duration: 4000 });
        },
      });
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
      // Portion cook factors are an enhancement — a failure must not block the list.
      factors: this.portionService
        .getRangeCookFactors(
          this.householdId(),
          ShoppingComponent.toDateString(today),
          ShoppingComponent.toDateString(ShoppingComponent.addDays(today, this.daysAhead() - 1)),
        )
        .pipe(catchError(() => of([]))),
      shopping: this.householdShopping
        .getState(this.householdId())
        .pipe(catchError(() => of({ items: [], checks: [] } as ShoppingState))),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ weeks, pantry, packaging, measurements, factors, shopping }) => {
          this.weekDataList.set(weeks);
          this.pantryItems.set(pantry);
          this.retailPackages.set(packaging);
          this.measurements.set(measurements);
          this.cookFactors.set(
            new Map(factors.map((f) => [`${f.date}|${f.mealTypeId}|${f.recipeId}`, f.cookFactor])),
          );
          this.applyShoppingState(shopping);
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
      this.afterListLoaded();
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
          this.afterListLoaded();
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
        if (item.source !== 'plan') continue;
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

  private applyShoppingState(state: ShoppingState): void {
    this.adhocItems.set(state.items);
    const checked = new Set(state.checks.map((c) => c.itemKey));
    for (const item of state.items) if (item.checked) checked.add(adhocCheckKey(item.id));
    this.checkedItems.set(checked);
  }

  private afterListLoaded(): void {
    this.migrateLegacyChecks();
    this.fetchAskable();
  }

  /**
   * Ticks made before check state moved to the server lived in a per-day
   * localStorage key. Push today's over once, then forget the key.
   */
  private migrateLegacyChecks(): void {
    const key = `nom-shopping-${this.householdId()}-${ShoppingComponent.toDateString(new Date())}-${this.daysAhead()}`;
    let legacy: string[];
    try {
      const stored = localStorage.getItem(key);
      if (!stored) return;
      legacy = JSON.parse(stored) as string[];
      localStorage.removeItem(key);
    } catch {
      return;
    }
    const onList = new Set<string>();
    for (const dept of this.departments()) for (const item of dept.items) onList.add(item.checkKey);
    for (const itemKey of legacy) {
      if (!onList.has(itemKey) || this.checkedItems().has(itemKey)) continue;
      this.setCheckedLocally(itemKey, true);
      this.householdShopping
        .setChecked({ householdId: this.householdId(), itemKey, checked: true })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ error: () => undefined });
    }
  }

  private fetchAskable(): void {
    const ids = new Set<number>();
    for (const dept of this.departments()) {
      for (const item of dept.items) {
        if (item.ingredientId !== null && !this.checkedItems().has(item.checkKey)) ids.add(item.ingredientId);
      }
    }
    if (ids.size === 0) {
      this.askable.set(new Set());
      return;
    }
    this.householdShopping
      .needsVerification(this.householdId(), [...ids].slice(0, 200))
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((rows) => {
        this.askable.set(new Set(rows.filter((r) => r.shouldAsk).map((r) => r.ingredientId)));
      });
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
