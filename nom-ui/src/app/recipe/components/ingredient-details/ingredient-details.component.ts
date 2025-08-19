import { Component, Input } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { MatDividerModule } from '@angular/material/divider';
import { IngredientModel } from '../../models/ingredient.model';

import { BaseDetailComponent, BaseDetailConfig } from '../../../common/components/base-detail/base-detail.component';

// Import the new label component and its data model
import { NutritionLabelComponent } from '../../../nutrient/components/nutrition-label/nutrition-label.component';
import { NutritionLabelData } from '../../../nutrient/models/nutrition-label-data';
import { LabelNutrient } from '../../../nutrient/models/label-nutrient';

@Component({
  selector: 'nom-ingredient-details',
  standalone: true,
  imports: [
    CommonModule,
    MatDividerModule,
    NutritionLabelComponent,
    BaseDetailComponent,
  ],
  providers: [DecimalPipe],
  templateUrl: './ingredient-details.component.html',
  styleUrls: ['./ingredient-details.component.scss'],
})
export class IngredientDetailsComponent {
  // This property will hold the formatted data for the label component
  public nutritionLabelData: NutritionLabelData | null = null;

  private _ingredient: IngredientModel | null = null;

  detailConfig: BaseDetailConfig = {
    title: 'Ingredient Details',
    subtitle: 'Nutritional information and details',
    showBackButton: true,
    backButtonText: 'Back',
    maxWidth: '800px'
  };

  @Input()
  set ingredient(value: IngredientModel | null) {
    this._ingredient = value;
    if (value) {
      // When the ingredient is set, map it to the label data structure
      this.nutritionLabelData = this.mapToLabelData(value);
      this.detailConfig = {
        title: value.name,
        subtitle: `FDC ID: ${value.fdcId}`,
        showBackButton: true,
        backButtonText: 'Back',
        maxWidth: '800px'
      };
    } else {
      this.nutritionLabelData = null;
    }
  }

  get ingredient(): IngredientModel | null {
    return this._ingredient;
  }

  onBack(): void {
    // This will be handled by the parent component
    console.log('Back button clicked');
  }

  /**
   * Transforms the IngredientModel into the NutritionLabelData structure.
   */
  private mapToLabelData(ingredient: IngredientModel): NutritionLabelData {
    const boldNutrients = ['Total Fat', 'Cholesterol', 'Sodium', 'Total Carbohydrate', 'Protein'];
    const indentedNutrients = ['Saturated Fat', 'Trans Fat', 'Dietary Fiber', 'Total Sugars', 'Includes Added Sugars'];

    const nutrients: LabelNutrient[] = ingredient.nutrients.map(nutrient => ({
      name: nutrient.nutrientName,
      amount: nutrient.amount,
      unit: nutrient.unitName,
      dailyValue: this.calculateDailyValue(nutrient.nutrientName, nutrient.amount),
      isBold: boldNutrients.includes(nutrient.nutrientName),
      isIndented: indentedNutrients.includes(nutrient.nutrientName),
    }));

    return {
      // NOTE: Serving size data is not in the current model, so we are using static values.
      servingsPerContainer: '',
      servingSizeHousehold: '100 g',
      servingSizeGrams: 100, // Based on the "per 100g" note
      calories: nutrients.find(n => n.unit === 'kcal')?.amount || 0,
      nutrients: nutrients,
    };
  }

  /**
   * Calculate daily value percentage for a nutrient.
   * This is a simplified calculation based on FDA guidelines.
   */
  private calculateDailyValue(nutrientName: string, amount: number): number {
    const dailyValues: Record<string, number> = {
      'Total Fat': 78, // g
      'Saturated Fat': 20, // g
      'Trans Fat': 2, // g
      'Cholesterol': 300, // mg
      'Sodium': 2300, // mg
      'Total Carbohydrate': 275, // g
      'Dietary Fiber': 28, // g
      'Total Sugars': 50, // g
      'Protein': 50, // g
      'Vitamin D': 20, // mcg
      'Calcium': 1300, // mg
      'Iron': 18, // mg
      'Potassium': 4700, // mg
    };

    const dailyValue = dailyValues[nutrientName];
    if (!dailyValue) return 0;

    return Math.round((amount / dailyValue) * 100);
  }
}