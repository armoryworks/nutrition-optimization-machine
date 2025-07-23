import { Component, Input } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { IngredientModel } from '../../models/ingredient.model';
import { NutrientValueModel } from '../../models/nutrient-value.model';

// Import the new label component and its data model
import { NutritionLabelComponent } from '../../../nutrient/components/nutrition-label/nutrition-label.component';
import { NutritionLabelData } from '../../../nutrient/models/nutrition-label-data';
import { LabelNutrient } from '../../../nutrient/models/label-nutrient';


@Component({
  selector: 'app-ingredient-details',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatDividerModule,
    NutritionLabelComponent, // Import the new component
  ],
  providers: [DecimalPipe],
  templateUrl: './ingredient-details.component.html',
  styleUrls: ['./ingredient-details.component.scss'],
})
export class IngredientDetailsComponent {
  // This property will hold the formatted data for the label component
  public nutritionLabelData: NutritionLabelData | null = null;
  
  private _ingredient: IngredientModel | null = null;

  @Input()
  set ingredient(value: IngredientModel | null) {
    this._ingredient = value;
    if (value) {
      // When the ingredient is set, map it to the label data structure
      this.nutritionLabelData = this.mapToLabelData(value);
    } else {
      this.nutritionLabelData = null;
    }
  }

  get ingredient(): IngredientModel | null {
    return this._ingredient;
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
      dailyValue: 0, // Placeholder for DV%, which requires another data source
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
}