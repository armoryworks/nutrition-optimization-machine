import { Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IngredientService } from '../core/services/ingredient.service';
import { LoadingService } from '../core/services/loading.service';
import { IngredientEditModel } from '../core/models/ingredient.model';

@Component({
  selector: 'nom-my-ingredients',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="nom-dashboard">
      <div class="nom-dashboard__header">
        <div class="nom-dashboard__header-left">
          <h1 class="nom-dashboard__title">My Ingredients</h1>
          <span class="nom-dashboard__subtitle">{{ ingredients().length }} ingredients</span>
        </div>
        <div class="nom-dashboard__header-right">
          <a mat-flat-button routerLink="/ingredient/new">
            <mat-icon>add</mat-icon>
            New Ingredient
          </a>
        </div>
      </div>

      @if (loading()) {
        <div class="nom-dashboard__loading">
          <mat-spinner diameter="40"></mat-spinner>
        </div>
      } @else if (ingredients().length === 0) {
        <div class="nom-dashboard__empty">
          <mat-icon class="nom-dashboard__empty-icon">egg</mat-icon>
          <h2 class="nom-dashboard__empty-title">No ingredients yet</h2>
          <p class="nom-dashboard__empty-message">Create custom ingredients to use in your recipes.</p>
          <a mat-flat-button routerLink="/ingredient/new" class="nom-dashboard__empty-action">
            <mat-icon>add</mat-icon>
            Create Ingredient
          </a>
        </div>
      } @else {
        <div class="nom-ingredients__list">
          @for (ing of ingredients(); track ing.id) {
            <a class="nom-ingredients__item" [routerLink]="['/ingredient', ing.id, 'edit']">
              <div class="nom-ingredients__item-info">
                <span class="nom-ingredients__item-name">{{ ing.name }}</span>
                @if (ing.description) {
                  <span class="nom-ingredients__item-desc">{{ ing.description }}</span>
                }
                @if (ing.aliases.length > 0) {
                  <span class="nom-ingredients__item-aliases">
                    Also: {{ aliasNames(ing) }}
                  </span>
                }
              </div>
              @if (ing.curationStatusName) {
                <span class="nom-ingredients__item-status">{{ ing.curationStatusName }}</span>
              }
              <mat-icon class="nom-ingredients__item-arrow">chevron_right</mat-icon>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-ingredients__list {
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-2;
    }

    .nom-ingredients__item {
      display: flex;
      align-items: center;
      gap: vars.$spacing-3;
      padding: vars.$spacing-3 vars.$spacing-4;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: vars.$nom-border-radius;
      background: var(--mat-sys-surface);
      cursor: pointer;
      text-decoration: none;
      color: inherit;
      transition: background vars.$transition-duration-fast vars.$transition-timing;

      &:hover {
        background: var(--mat-sys-surface-container);
      }
    }

    .nom-ingredients__item-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-1;
      min-width: 0;
    }

    .nom-ingredients__item-name {
      font-weight: vars.$font-weight-medium;
      color: var(--mat-sys-on-surface);
    }

    .nom-ingredients__item-desc {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nom-ingredients__item-aliases {
      font-size: vars.$font-size-xs;
      color: var(--mat-sys-on-surface-variant);
      font-style: italic;
    }

    .nom-ingredients__item-status {
      font-size: vars.$font-size-xs;
      padding: vars.$spacing-1 vars.$spacing-2;
      border-radius: vars.$nom-border-radius-pill;
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
      white-space: nowrap;
    }

    .nom-ingredients__item-arrow {
      color: var(--mat-sys-on-surface-variant);
      flex-shrink: 0;
    }

    .nom-dashboard__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-12;
    }
  `],
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
