import { Component, computed, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RecipeNutritionModel, RecipeNutritionSource } from '../../../core/models/recipe-nutrition.model';

interface NutrientDisplayRow {
  displayName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number | null;
  partial: boolean;
  isChild: boolean;
  bold: boolean;
}

export interface UncountedIngredient {
  name: string;
  reason: 'no-data' | 'no-conversion';
}

interface NutrientDisplayConfig {
  names: string[];
  displayName: string;
  isChild: boolean;
  isMacro: boolean;
}

const FDA_NUTRIENT_ORDER: NutrientDisplayConfig[] = [
  { names: ['total lipid (fat)', 'total fat', 'fat'], displayName: 'Total Fat', isChild: false, isMacro: true },
  { names: ['fatty acids, total saturated', 'saturated fat'], displayName: 'Saturated Fat', isChild: true, isMacro: true },
  { names: ['fatty acids, total trans', 'trans fat'], displayName: 'Trans Fat', isChild: true, isMacro: true },
  { names: ['cholesterol'], displayName: 'Cholesterol', isChild: false, isMacro: true },
  { names: ['sodium, na', 'sodium'], displayName: 'Sodium', isChild: false, isMacro: true },
  { names: ['carbohydrate, by difference', 'total carbohydrate', 'carbohydrates', 'carbs'], displayName: 'Total Carbohydrate', isChild: false, isMacro: true },
  { names: ['fiber, total dietary', 'dietary fiber', 'fiber'], displayName: 'Dietary Fiber', isChild: true, isMacro: true },
  { names: ['sugars, total', 'total sugars'], displayName: 'Total Sugars', isChild: true, isMacro: true },
  { names: ['added sugars'], displayName: 'Added Sugars', isChild: true, isMacro: true },
  { names: ['protein'], displayName: 'Protein', isChild: false, isMacro: true },
  { names: ['vitamin d (d2 + d3)', 'vitamin d'], displayName: 'Vitamin D', isChild: false, isMacro: false },
  { names: ['calcium, ca', 'calcium'], displayName: 'Calcium', isChild: false, isMacro: false },
  { names: ['iron, fe', 'iron'], displayName: 'Iron', isChild: false, isMacro: false },
  { names: ['potassium, k', 'potassium'], displayName: 'Potassium', isChild: false, isMacro: false },
];

const KNOWN_NUTRIENT_NAMES = new Set(FDA_NUTRIENT_ORDER.flatMap(c => c.names));

@Component({
  selector: 'nom-nutrition-label',
  imports: [DecimalPipe],
  templateUrl: './nutrition-label.component.html',
  styleUrl: './nutrition-label.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NutritionLabel {
  nutrition = input<RecipeNutritionModel[]>([]);
  servings = input<number | undefined>(undefined);
  /** Ingredient names for the label's "Ingredients:" line (sub-ingredient
   *  detail can replace this once the data model carries it). */
  ingredientNames = input<string[]>([]);
  /** Measured ingredient lines the derived label could not count, so the totals are low. */
  uncounted = input<UncountedIngredient[]>([]);
  /** Where the label's numbers come from, for the attribution line. */
  source = input<RecipeNutritionSource | null | undefined>(null);

  /** Mutually exclusive label views: standard facts, ingredients, vitamins/minerals. */
  view = signal<'facts' | 'ingredients' | 'micros'>('facts');

  hasData = computed(() => this.nutrition().length > 0);

  /** Per-serving amount from the recipe record (preferred source). */
  servingQuantity = input<number | undefined>(undefined);
  servingUnit = input<string | undefined>(undefined);

  /** Per-serving weight: recipe record first, nutrient-row fallback. */
  servingWeight = computed(() => {
    const q = this.servingQuantity();
    if (q) return `${q} ${this.servingUnit() || 'g'}`;
    const match = this.findNutrient(this.nutrition(), ['serving weight', 'serving size']);
    if (!match) return '';
    return `${Math.round(match.amount)} ${match.unit || 'g'}`;
  });

  calories = computed(() => {
    const data = this.nutrition();
    return this.findNutrient(data, ['energy', 'calories']);
  });

  macroNutrients = computed(() => this.buildRows(true));
  microNutrients = computed(() => this.buildRows(false));

  /** True when a shown nutrient is a minimum because some counted ingredients lack it. */
  hasPartial = computed(() => this.calories()?.partial === true
    || [...this.macroNutrients(), ...this.microNutrients()].some(r => r.partial));

  sourceText = computed(() => {
    const s = this.source();
    switch (s?.kind) {
      case 'usda':
        return 'Nutrition based on USDA FoodData Central: every counted ingredient is linked to a USDA food.';
      case 'mixed':
        return `Nutrition based on USDA FoodData Central for ${s.usdaLines} of ${s.countedLines} counted ingredients; the rest from NOM's own ingredient data.`;
      case 'catalog':
        return "Nutrition from NOM's own ingredient data, not linked to USDA FoodData Central.";
      case 'recipe':
        return 'Nutrition as entered with this recipe.';
      default:
        return '';
    }
  });

  private buildRows(macro: boolean): NutrientDisplayRow[] {
    const data = this.nutrition();
    const rows: NutrientDisplayRow[] = [];

    for (const config of FDA_NUTRIENT_ORDER) {
      if (config.isMacro !== macro) continue;

      const match = this.findNutrient(data, config.names);
      if (match) {
        rows.push({
          displayName: config.displayName,
          amount: match.amount,
          unit: match.unit,
          dailyValuePercent: match.dailyValuePercent,
          partial: match.partial === true,
          isChild: config.isChild,
          bold: !config.isChild,
        });
      }
    }

    return rows;
  }

  private findNutrient(data: RecipeNutritionModel[], names: string[]): RecipeNutritionModel | undefined {
    const lowerNames = names.map(n => n.toLowerCase());
    return data.find(n => lowerNames.includes(n.nutrientName.toLowerCase()))
      ?? data.find(n => {
        const name = n.nutrientName.toLowerCase();
        return !KNOWN_NUTRIENT_NAMES.has(name) && lowerNames.some(ln => name.includes(ln));
      });
  }
}
