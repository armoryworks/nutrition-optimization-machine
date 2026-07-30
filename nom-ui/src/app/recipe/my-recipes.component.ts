import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RecipeService } from '../core/services/recipe.service';
import { LoadingService } from '../core/services/loading.service';
import { RecipeModel } from '../core/models/recipe.model';

@Component({
  selector: 'nom-my-recipes',
  imports: [RouterLink, DatePipe, DecimalPipe, MatIconModule, MatButtonModule, MatProgressSpinnerModule, ErrorBanner],
  templateUrl: './my-recipes.component.html',
  styleUrl: './my-recipes.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyRecipes implements OnInit {
  private recipeService = inject(RecipeService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  recipes = signal<RecipeModel[]>([]);
  loading = signal(true);
  error = signal('');

  ngOnInit(): void {
    this.recipeService.getMyRecipes().pipe(
      this.loadingService.loading('Loading your recipes...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (recipes) => {
        this.recipes.set(recipes);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load your recipes.');
        this.loading.set(false);
      },
    });
  }
}
