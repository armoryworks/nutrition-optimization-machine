import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RecipeNutritionModel } from '../../../core/models/recipe.model';

interface NutrientDisplayRow {
  displayName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number;
  isChild: boolean;
  bold: boolean;
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

@Component({
  selector: 'nom-nutrition-label',
  imports: [DecimalPipe],
  templateUrl: './nutrition-label.component.html',
  styleUrl: './nutrition-label.component.scss'
})
export class NutritionLabel {
  nutrition = input<RecipeNutritionModel[]>([]);
  servings = input<number | undefined>(undefined);

  hasData = computed(() => this.nutrition().length > 0);

  calories = computed(() => {
    const data = this.nutrition();
    return this.findNutrient(data, ['energy', 'calories']);
  });

  macroNutrients = computed(() => this.buildRows(true));
  microNutrients = computed(() => this.buildRows(false));

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
          isChild: config.isChild,
          bold: !config.isChild,
        });
      }
    }

    return rows;
  }

  private findNutrient(data: RecipeNutritionModel[], names: string[]): RecipeNutritionModel | undefined {
    const lowerNames = names.map(n => n.toLowerCase());
    return data.find(n => {
      const name = n.nutrientName.toLowerCase();
      return lowerNames.some(ln => name === ln || name.includes(ln));
    });
  }
}
