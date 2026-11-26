import { Component, computed, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RecipeAlcoholNote, RecipeNutritionModel, RecipeNutritionSource, RetentionBoundReason } from '../../../core/models/recipe-nutrition.model';

const BOUND_REASONS: Record<RetentionBoundReason, string> = {
  parts: 'The recipe has more than one part, and the steps do not make clear that the part with the alcohol is the one cooked',
  pause: 'The part with the alcohol is set aside or chilled before the cooking, and the steps do not make clear that the cooking is of that part',
  unnamed: 'The steps never say when the alcohol goes in',
  lines: 'The alcohol goes in at more than one point, and the steps do not say how much at each',
  choice: 'The steps offer a choice of time or method',
  flamed: 'Flamed, and how long it burns is not stated',
  marinade: 'Used to soak or marinate, and how much goes on with the food, and how, is not stated',
};

interface NutrientDisplayRow {
  displayName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number | null;
  partial: boolean;
  isChild: boolean;
  bold: boolean;
  digits: string;
}

export interface UncountedIngredient {
  name: string;
  reason: 'no-data' | 'no-conversion';
}

export interface AssumedIngredient {
  name: string;
  size: string;
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
  { names: ['vitamin a, rae', 'vitamin a'], displayName: 'Vitamin A', isChild: false, isMacro: false },
  { names: ['vitamin c, total ascorbic acid', 'vitamin c'], displayName: 'Vitamin C', isChild: false, isMacro: false },
  { names: ['vitamin e (alpha-tocopherol)', 'vitamin e'], displayName: 'Vitamin E', isChild: false, isMacro: false },
  { names: ['vitamin k (phylloquinone)', 'vitamin k'], displayName: 'Vitamin K', isChild: false, isMacro: false },
  { names: ['thiamin'], displayName: 'Thiamin', isChild: false, isMacro: false },
  { names: ['riboflavin'], displayName: 'Riboflavin', isChild: false, isMacro: false },
  { names: ['niacin'], displayName: 'Niacin', isChild: false, isMacro: false },
  { names: ['vitamin b-6', 'vitamin b6'], displayName: 'Vitamin B6', isChild: false, isMacro: false },
  { names: ['folate, dfe', 'folate'], displayName: 'Folate', isChild: false, isMacro: false },
  { names: ['vitamin b-12', 'vitamin b12'], displayName: 'Vitamin B12', isChild: false, isMacro: false },
  { names: ['biotin'], displayName: 'Biotin', isChild: false, isMacro: false },
  { names: ['pantothenic acid'], displayName: 'Pantothenic Acid', isChild: false, isMacro: false },
  { names: ['phosphorus, p', 'phosphorus'], displayName: 'Phosphorus', isChild: false, isMacro: false },
  { names: ['iodine, i', 'iodine'], displayName: 'Iodine', isChild: false, isMacro: false },
  { names: ['magnesium, mg', 'magnesium'], displayName: 'Magnesium', isChild: false, isMacro: false },
  { names: ['zinc, zn', 'zinc'], displayName: 'Zinc', isChild: false, isMacro: false },
  { names: ['selenium, se', 'selenium'], displayName: 'Selenium', isChild: false, isMacro: false },
  { names: ['copper, cu', 'copper'], displayName: 'Copper', isChild: false, isMacro: false },
  { names: ['manganese, mn', 'manganese'], displayName: 'Manganese', isChild: false, isMacro: false },
  { names: ['chromium, cr', 'chromium'], displayName: 'Chromium', isChild: false, isMacro: false },
  { names: ['molybdenum, mo', 'molybdenum'], displayName: 'Molybdenum', isChild: false, isMacro: false },
  { names: ['chloride'], displayName: 'Chloride', isChild: false, isMacro: false },
  { names: ['choline, total', 'choline'], displayName: 'Choline', isChild: false, isMacro: false },
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
  /** Lines counted at an assumed container size, so the totals are an estimate. */
  assumed = input<AssumedIngredient[]>([]);
  /** Where the label's numbers come from, for the attribution line. */
  source = input<RecipeNutritionSource | null | undefined>(null);

  /** The alcohol note under the label: drinks as written and after cooking, and any flavouring. */
  alcohol = input<RecipeAlcoholNote | null | undefined>(null);

  /** Mutually exclusive label views: standard facts, ingredients, vitamins/minerals. */
  view = signal<'facts' | 'ingredients' | 'micros'>('facts');

  hasData = computed(() => this.nutrition().length > 0);

  /** True when a counted line was weighed at an assumed container size. */
  estimated = computed(() => this.assumed().length > 0 || (this.source()?.assumedLines ?? 0) > 0);

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

  alcoholDrinksText = computed(() => {
    const a = this.alcohol();
    if (!a || a.drinks.length === 0) return '';
    return `Contains alcohol: ${a.drinks.join(', ')}. As written, about ${this.grams(a.asWrittenGrams)} alcohol ${this.per(a)}.`;
  });

  alcoholCookingText = computed(() => {
    const a = this.alcohol();
    if (!a || a.drinks.length === 0) return '';
    const stays = `${this.grams(a.retainedGrams)} ${this.per(a)}`;
    const pct = a.retainedPercent ?? 100;
    if (a.notRead) return 'How it is cooked has not been read yet, so all of the alcohol is counted.';
    if (a.basis === 'up-to' && a.reason) {
      return `${BOUND_REASONS[a.reason]}, so up to ${pct}% may stay (USDA retention table): up to ${stays}. This is an upper bound; calories count only the alcohol that may stay.`;
    }
    const others = a.upTo && a.basis !== 'up-to' ? ` Another drink line can only be bounded, so in all up to ${stays} may stay.` : '';
    return this.cookingText(a, pct, stays) + others;
  });

  private cookingText(a: RecipeAlcoholNote, pct: number, stays: string): string {
    switch (a.basis) {
      case 'timed':
        return `${a.method === 'bake' ? 'Baked' : 'Simmered'} about ${a.minutes} minutes, so about ${pct}% stays (USDA retention table): about ${stays}. Calories count only the alcohol that stays.`;
      case 'up-to':
        return a.minutes
          ? `Cooked only about ${a.minutes} minutes, so up to ${pct}% may stay (USDA retention table): up to ${stays}. Calories count only the alcohol that stays.`
          : `Cooking time not stated, so up to ${pct}% may stay (USDA retention table): up to ${stays}. Calories count only the alcohol that stays.`;
      case 'flamed':
        return `Flamed, so about ${pct}% stays (USDA retention table): about ${stays}. Calories count only the alcohol that stays.`;
      case 'hot-liquid':
        return `Stirred into a hot dish off the heat, so about ${pct}% stays (USDA retention table): about ${stays}. Calories count only the alcohol that stays.`;
      case 'overnight':
        return `Not cooked, but left to stand overnight, so about ${pct}% stays (USDA retention table): about ${stays}. Calories count only the alcohol that stays.`;
      case 'no-steps':
        return 'The recipe gives no method, so all of the alcohol is counted.';
      default:
        return 'Not cooked after the alcohol is added, so all of it stays.';
    }
  }

  alcoholFlavouringText = computed(() => {
    const a = this.alcohol();
    if (!a || a.flavourings.length === 0) return '';
    const amount = a.flavouringGrams < 0.5 ? 'under 0.5 g' : `about ${this.grams(a.flavouringGrams)}`;
    return `${a.flavourings.join(', ')} ${a.flavourings.length === 1 ? 'adds' : 'add'} a trace of alcohol (${amount} ${this.per(a)}).`;
  });

  alcoholUncountedText = computed(() => {
    const a = this.alcohol();
    if (!a || a.uncounted.length === 0) return '';
    return `The alcohol in ${a.uncounted.map(u => `“${u}”`).join(', ')} can't be counted yet, so a household that avoids alcohol is not offered this recipe.`;
  });

  private grams(value: number): string {
    return `${value < 10 ? Math.round(value * 10) / 10 : Math.round(value)} g`;
  }

  private per(a: RecipeAlcoholNote): string {
    return a.perServing ? 'per serving' : 'in the whole recipe';
  }

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
          digits: !macro && match.amount < 1 ? '1.0-2' : '1.0-1',
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
