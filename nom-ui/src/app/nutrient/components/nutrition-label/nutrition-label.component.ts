import { Component, Input, computed, signal } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';

// Import the pipe
import { FormatMassPipe } from '../../../common/pipes/format-mass.pipe';
import { NutritionLabelData } from '../../models/nutrition-label-data';
import { LabelNutrient } from '../../models/label-nutrient';

@Component({
  selector: 'nom-nutrition-label',
  standalone: true,
  imports: [CommonModule, FormatMassPipe],
  providers: [DecimalPipe],
  templateUrl: './nutrition-label.component.html',
  styleUrls: ['./nutrition-label.component.scss'],
})
export class NutritionLabelComponent {
  private _data = signal<NutritionLabelData | null>(null);

  @Input()
  set data(value: NutritionLabelData | null) {
    this._data.set(value);
  }
  get data(): NutritionLabelData | null {
    return this._data();
  }

  // USDA-required micronutrients (vitamins and minerals)
  private readonly micronutrientNames = new Set([
    'Vitamin D',
    'Calcium',
    'Iron',
    'Potassium',
    'Vitamin A',
    'Vitamin C',
    'Vitamin E',
    'Vitamin K',
    'Thiamin',
    'Riboflavin',
    'Niacin',
    'Vitamin B6',
    'Folate',
    'Vitamin B12',
    'Biotin',
    'Pantothenic Acid',
    'Phosphorus',
    'Iodine',
    'Magnesium',
    'Zinc',
    'Selenium',
    'Copper',
    'Manganese',
    'Chromium',
    'Molybdenum',
    'Chloride',
  ]);

  // Macronutrients (Fat, Cholesterol, Sodium, Carbs, Protein and their sub-nutrients)
  macronutrients = computed<LabelNutrient[]>(() => {
    const nutrients = this._data()?.nutrients || [];
    return nutrients.filter(n => !this.micronutrientNames.has(n.name));
  });

  // Micronutrients (Vitamins and Minerals)
  micronutrients = computed<LabelNutrient[]>(() => {
    const nutrients = this._data()?.nutrients || [];
    return nutrients.filter(n => this.micronutrientNames.has(n.name));
  });
}