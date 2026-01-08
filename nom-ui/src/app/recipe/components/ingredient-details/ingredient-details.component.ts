import { Component, inject, input, computed } from '@angular/core';
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

  ingredient = input<IngredientModel | null>(null);

  // Computed signal for nutrition label data derived from ingredient
  nutritionLabelData = computed(() => {
    const ing = this.ingredient();
    return ing ? this.mapToLabelData(ing) : null;
  });

  // Computed signal for detail config derived from ingredient
  detailConfig = computed(() => {
    const ing = this.ingredient();
    if (ing) {
      return {
        title: ing.name,
        subtitle: `FDC ID: ${ing.fdcId}`,
        showBackButton: true,
        backButtonText: 'Back',
        maxWidth: '800px'
      };
    }
    return {
      title: 'Ingredient Details',
      subtitle: 'Nutritional information and details',
      showBackButton: true,
      backButtonText: 'Back',
      maxWidth: '800px'
    };
  });

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