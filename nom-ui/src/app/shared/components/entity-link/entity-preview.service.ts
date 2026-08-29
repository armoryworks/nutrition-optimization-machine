import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { RecipeService } from '../../../core/services/recipe.service';
import { RecipeModel } from '../../../core/models/recipe.model';

/**
 * Cached recipe fetches for link hover previews. Uses the normal authorized
 * recipe endpoint, so server-side visibility rules are inherited — a preview
 * can never show a recipe the caller couldn't open.
 */
@Injectable({ providedIn: 'root' })
export class EntityPreviewService {
  private recipeService = inject(RecipeService);
  private cache = new Map<number, Observable<RecipeModel>>();

  recipe(id: number): Observable<RecipeModel> {
    let obs = this.cache.get(id);
    if (!obs) {
      obs = this.recipeService.getRecipe(id).pipe(shareReplay({ bufferSize: 1, refCount: false }));
      this.cache.set(id, obs);
    }
    return obs;
  }
}
