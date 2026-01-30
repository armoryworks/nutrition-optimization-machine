import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwIconComponent, AmwCardComponent, AmwPopoverComponent } from 'angular-material-wrap';
import { RecipeService } from '../../services/recipe.service';
import { NutritionLabelComponent } from '../../../nutrient/components/nutrition-label/nutrition-label.component';
import { NutritionLabelData } from '../../../nutrient/models/nutrition-label-data';
import { LabelNutrient } from '../../../nutrient/models/label-nutrient';

interface RecipeDetail {
  id: number;
  name: string;
  description?: string;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  totalTime?: number;
  servings?: number;
  authorName?: string;
  imageUrl?: string;
  averageRating?: number;
  ratingCount?: number;
  ingredients?: RecipeIngredient[];
  steps?: RecipeStep[];
  nutrition?: RecipeNutrition[];
  categories?: string[];
  tags?: string[];
}

interface RecipeIngredient {
  name: string;
  quantity?: number;
  measurement?: string;
  notes?: string;
}

interface RecipeStep {
  stepNumber: number;
  description: string;
}

interface RecipeNutrition {
  nutrientName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number;
}

@Component({
  selector: 'nom-recipe-detail-public',
  standalone: true,
  imports: [
    CommonModule,
    AmwButtonComponent,
    AmwIconComponent,
    AmwCardComponent,
    AmwPopoverComponent,
    NutritionLabelComponent
  ],
  templateUrl: './recipe-detail-public.component.html',
  styleUrls: ['./recipe-detail-public.component.scss']
})
export class RecipeDetailPublicComponent implements OnInit, OnDestroy {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private recipeService = inject(RecipeService);
  private destroy$ = new Subject<void>();

  recipe = signal<RecipeDetail | null>(null);
  isLoading = signal(true);
  hasError = signal(false);
  errorMessage = signal('');
  nutritionPopoverOpen = signal(false);

  // Popover trigger configuration - standard close behavior
  nutritionPopoverTrigger = {
    type: 'click' as const,
    toggle: true,
    escapeKey: false,
    outsideClick: false
  };

  // Fallback placeholder image - plate and utensils SVG
  private readonly placeholderImage = `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='400' height='400' viewBox='0 0 400 400'%3E%3Crect fill='%23374151' width='400' height='400'/%3E%3Cg fill='%239CA3AF' transform='translate(100,100)'%3E%3C!-- Plate --%3E%3Ccircle cx='100' cy='100' r='90' fill='none' stroke='%239CA3AF' stroke-width='8'/%3E%3Ccircle cx='100' cy='100' r='60' fill='none' stroke='%239CA3AF' stroke-width='4'/%3E%3C!-- Fork --%3E%3Cpath d='M30 40 L30 100 M30 40 L30 20 M25 20 L25 50 M35 20 L35 50 M20 20 L20 45 M40 20 L40 45' stroke='%239CA3AF' stroke-width='4' stroke-linecap='round' fill='none'/%3E%3C!-- Knife --%3E%3Cpath d='M170 40 L170 100 M170 20 L175 60 L170 60 Z' stroke='%239CA3AF' stroke-width='4' stroke-linecap='round' fill='%239CA3AF'/%3E%3C/g%3E%3C/svg%3E`;

  recipeImageUrl = computed(() => {
    return this.recipe()?.imageUrl || this.placeholderImage;
  });

  // Quick macro summary for the popover
  macroSummary = computed(() => {
    const nutrition = this.recipe()?.nutrition;
    if (!nutrition?.length) return null;

    const findNutrient = (name: string) => nutrition.find(n => n.nutrientName === name);

    return {
      calories: findNutrient('Calories') || findNutrient('Energy'),
      fat: findNutrient('Fat'),
      carbs: findNutrient('Total Carbohydrates'),
      protein: findNutrient('Protein')
    };
  });

  nutritionLabelData = computed<NutritionLabelData | null>(() => {
    const r = this.recipe();
    if (!r?.nutrition?.length) return null;

    // Define which nutrients should be bold (main nutrients) and which are indented (sub-nutrients)
    const mainNutrients = ['Fat', 'Total Carbohydrates', 'Protein', 'Cholesterol', 'Sodium'];
    const subNutrients = ['Saturated Fat', 'Dietary Fiber', 'Added Sugars'];

    const nutrients: LabelNutrient[] = r.nutrition.map(n => ({
      name: n.nutrientName,
      amount: n.amount,
      unit: n.unit,
      dailyValue: n.dailyValuePercent,
      isBold: mainNutrients.includes(n.nutrientName),
      isIndented: subNutrients.includes(n.nutrientName)
    }));

    return {
      servingsPerContainer: `${r.servings || 1} servings`,
      servingSizeHousehold: '1 serving',
      servingSizeGrams: 0, // Not tracked per recipe
      calories: 0, // TODO: Calculate from macros if needed
      nutrients
    };
  });

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
      const id = params['id'];
      if (id) {
        this.loadRecipe(Number(id));
      } else {
        this.hasError.set(true);
        this.errorMessage.set('Recipe ID not provided');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadRecipe(id: number): void {
    this.isLoading.set(true);
    this.hasError.set(false);

    this.recipeService.getRecipe(id).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.recipe.set(this.mapRecipeResponse(data));
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load recipe:', error);
        this.hasError.set(true);
        this.errorMessage.set(error?.status === 404
          ? 'Recipe not found'
          : 'Unable to load recipe. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  private mapRecipeResponse(data: any): RecipeDetail {
    return {
      id: data.id,
      name: data.name,
      description: data.description,
      prepTimeMinutes: data.prepTimeMinutes || data.prepTime,
      cookTimeMinutes: data.cookTimeMinutes || data.cookTime,
      totalTime: data.totalTime || ((data.prepTimeMinutes || 0) + (data.cookTimeMinutes || 0)),
      servings: data.servings || data.recipeServings,
      authorName: data.authorName || data.author?.name || 'Unknown',
      imageUrl: data.imageUrl || data.image,
      averageRating: data.averageRating || data.rating,
      ratingCount: data.ratingCount,
      ingredients: data.ingredients?.map((i: any) => ({
        name: i.name || i.ingredientName,
        quantity: i.quantity,
        measurement: i.measurement || i.measurementName,
        notes: i.notes || i.rawLine
      })) || [],
      steps: data.steps?.map((s: any, index: number) => ({
        stepNumber: s.stepNumber || s.order || index + 1,
        description: s.description || s.instructions
      })).sort((a: RecipeStep, b: RecipeStep) => a.stepNumber - b.stepNumber) || [],
      nutrition: data.nutrition?.map((n: any) => ({
        nutrientName: n.nutrientName,
        amount: n.amount,
        unit: n.unit,
        dailyValuePercent: n.dailyValuePercent
      })) || [],
      categories: data.categories || [],
      tags: data.tags || []
    };
  }

  formatTime(minutes: number | undefined): string {
    if (!minutes) return '';
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
  }

  formatQuantity(quantity: number | undefined): string {
    if (!quantity) return '';
    // Handle common fractions
    if (quantity === 0.25) return '1/4';
    if (quantity === 0.33 || quantity === 0.333) return '1/3';
    if (quantity === 0.5) return '1/2';
    if (quantity === 0.66 || quantity === 0.667) return '2/3';
    if (quantity === 0.75) return '3/4';
    // Return as number, removing trailing zeros
    return quantity % 1 === 0 ? quantity.toString() : quantity.toFixed(2).replace(/\.?0+$/, '');
  }

  goBack(): void {
    this.router.navigate(['/search']);
  }

  retry(): void {
    const id = this.route.snapshot.params['id'];
    if (id) {
      this.loadRecipe(Number(id));
    }
  }

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (img.src !== this.placeholderImage) {
      img.src = this.placeholderImage;
    }
  }
}
