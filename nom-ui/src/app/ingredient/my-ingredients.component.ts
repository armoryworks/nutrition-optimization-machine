import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IngredientService } from '../core/services/ingredient.service';
import { LoadingService } from '../core/services/loading.service';
import { IngredientEditModel } from '../core/models/ingredient-edit.model';

@Component({
  selector: 'nom-my-ingredients',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './my-ingredients.component.html',
  styleUrl: './my-ingredients.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyIngredients implements OnInit {
  private ingredientService = inject(IngredientService);
  private loadingService = inject(LoadingService);

  ingredients = signal<IngredientEditModel[]>([]);
  loading = signal(true);

  aliasNames(ing: IngredientEditModel): string {
    return ing.aliases.map(a => a.name).join(', ');
  }

  ngOnInit(): void {
    this.ingredientService.getMyIngredients().pipe(
      this.loadingService.loading('Loading ingredients...')
    ).subscribe({
      next: (data) => {
        this.ingredients.set(data);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
