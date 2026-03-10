import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { CookbookService } from '../core/services/cookbook.service';
import { LoadingService } from '../core/services/loading.service';
import { CookbookResponseModel } from '../core/models/cookbook-response.model';
import { RecipeModel } from '../core/models/recipe.model';
import { CookbookFormDialog, CookbookFormDialogData, CookbookFormDialogResult } from './cookbook-form-dialog.component';
import { ConfirmDeleteDialog, ConfirmDeleteDialogData } from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-cookbook-detail',
  standalone: true,
  imports: [
    RouterLink,

    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './cookbook-detail.component.html',
  styleUrls: ['./cookbook-detail.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CookbookDetail implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private cookbookService = inject(CookbookService);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);

  private destroyRef = inject(DestroyRef);

  cookbook = signal<CookbookResponseModel | null>(null);
  recipes = signal<RecipeModel[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  private cookbookId = 0;

  ngOnInit(): void {
    this.cookbookId = Number(this.route.snapshot.paramMap.get('id'));
    if (!this.cookbookId) {
      this.error.set('Invalid cookbook ID.');
      this.loading.set(false);
      return;
    }
    this.loadCookbook();
  }

  private loadCookbook(): void {
    this.loading.set(true);
    this.error.set(null);

    this.cookbookService.getCookbook(this.cookbookId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (cookbook) => {
        this.cookbook.set(cookbook);
        this.loadRecipes();
      },
      error: () => {
        this.error.set('Failed to load cookbook.');
        this.loading.set(false);
      },
    });
  }

  private loadRecipes(): void {
    this.cookbookService.getRecipes(this.cookbookId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (recipes) => {
        this.recipes.set(recipes);
        this.loading.set(false);
      },
      error: () => {
        this.recipes.set([]);
        this.loading.set(false);
      },
    });
  }

  onEdit(): void {
    const current = this.cookbook();
    if (!current) return;

    const dialogRef = this.dialog.open(CookbookFormDialog, {
      data: { cookbook: current } as CookbookFormDialogData,
    });

    dialogRef.afterClosed().subscribe((result: CookbookFormDialogResult | undefined) => {
      if (result) {
        this.cookbookService.updateCookbook(current.id, {
          name: result.name,
          description: result.description || undefined,
          isPublic: result.isPublic,
        }).subscribe({
          next: (updated) => {
            this.cookbook.set(updated);
          },
          error: () => this.error.set('Failed to update cookbook.'),
        });
      }
    });
  }

  onDelete(): void {
    const current = this.cookbook();
    if (!current) return;

    const dialogRef = this.dialog.open(ConfirmDeleteDialog, {
      data: {
        title: 'Delete Cookbook',
        message: `Are you sure you want to delete "${current.name}"? This cannot be undone.`,
        confirmText: 'Delete',
      } as ConfirmDeleteDialogData,
    });

    dialogRef.afterClosed().subscribe((confirmed: boolean) => {
      if (confirmed) {
        this.cookbookService.deleteCookbook(current.id).subscribe({
          next: () => this.router.navigate(['/cookbooks']),
          error: () => this.error.set('Failed to delete cookbook.'),
        });
      }
    });
  }

  onRemoveRecipe(recipeId: number): void {
    this.cookbookService.removeRecipe(this.cookbookId, recipeId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.recipes.update(list => list.filter(r => r.id !== recipeId));
        // Update the recipe count on the cookbook signal
        const current = this.cookbook();
        if (current) {
          this.cookbook.set({ ...current, recipeCount: current.recipeCount - 1 });
        }
      },
      error: () => this.error.set('Failed to remove recipe from cookbook.'),
    });
  }
}
