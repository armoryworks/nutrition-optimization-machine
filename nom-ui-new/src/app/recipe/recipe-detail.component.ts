import { Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { RecipeService } from '../core/services/recipe.service';
import { RecipeModel } from '../core/models/recipe.model';
import { NutritionLabel } from '../shared/components/nutrition-label/nutrition-label.component';

@Component({
  selector: 'nom-recipe-detail',
  imports: [MatIconModule, MatButtonModule, RouterLink, NutritionLabel],
  templateUrl: './recipe-detail.component.html',
  styleUrl: './recipe-detail.component.scss'
})
export class RecipeDetail implements OnInit {
  private route = inject(ActivatedRoute);
  private recipeService = inject(RecipeService);

  recipe = signal<RecipeModel | null>(null);
  loading = signal(true);
  error = signal('');
  activeTab = signal(0);

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      const id = Number(params['id']);
      if (isNaN(id)) {
        this.loading.set(false);
        return;
      }
      this.loadRecipe(id);
    });
  }

  loadRecipe(id: number): void {
    this.loading.set(true);
    this.error.set('');

    this.recipeService.getRecipe(id).subscribe({
      next: (recipe) => {
        this.recipe.set(recipe);
        this.loading.set(false);
      },
      error: (err) => {
        if (err.status === 404) {
          this.recipe.set(null);
        } else {
          this.error.set('Failed to load recipe.');
        }
        this.loading.set(false);
      }
    });
  }
}
