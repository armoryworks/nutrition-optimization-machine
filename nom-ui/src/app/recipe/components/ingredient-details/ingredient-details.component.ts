import { Component, Input, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { IngredientModel } from '../../models/ingredient.model';
import { ConfigurationService } from '../../../common/services/configuration.service';

@Component({
  selector: 'nom-ingredient-details',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
  ],
  providers: [DecimalPipe],
  templateUrl: './ingredient-details.component.html',
  styleUrls: ['./ingredient-details.component.scss'],
})
export class IngredientDetailsComponent {
  private configurationService = inject(ConfigurationService);

  // This property will hold the formatted data for the label component
  public nutritionLabelData: any = null;

  private _ingredient: IngredientModel | null = null;

  detailConfig: any = {
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
  private mapToLabelData(ingredient: IngredientModel): any {
    const boldNutrients = this.configurationService.BOLD_NUTRIENTS;
    const indentedNutrients = this.configurationService.INDENTED_NUTRIENTS;

    const nutrients: any[] = ingredient.nutrients.map(nutrient => ({
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
    const dailyValue = this.configurationService.getFDADailyValue(nutrientName);
    if (!dailyValue) return 0;

    return Math.round((amount / dailyValue) * 100);
  }
}