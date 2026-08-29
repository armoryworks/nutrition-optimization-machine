import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Params, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { RecipeModel } from '../../../core/models/recipe.model';
import { EntityPreviewService } from './entity-preview.service';

export interface EntityPreviewSheetData {
  recipeId: number;
  route: string | readonly unknown[];
  queryParams: Params | null;
}

/**
 * Phone presentation of the record-details preview: a bottom sheet curated for
 * touch — full-bleed image, generous type, and one full-width primary action.
 * Opened by nom-entity-link below the mobile breakpoint; the anchored popover
 * stays the desktop presentation.
 */
@Component({
  selector: 'nom-entity-preview-sheet',
  imports: [MatIconModule, MatButtonModule],
  templateUrl: './entity-preview-sheet.component.html',
  styleUrl: './entity-preview-sheet.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityPreviewSheet {
  private data = inject<EntityPreviewSheetData>(MAT_BOTTOM_SHEET_DATA);
  private sheetRef = inject(MatBottomSheetRef<EntityPreviewSheet>);
  private previewService = inject(EntityPreviewService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  recipe = signal<RecipeModel | null>(null);
  loadFailed = signal(false);

  constructor() {
    this.previewService
      .recipe(this.data.recipeId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (recipe) => this.recipe.set(recipe),
        error: () => this.loadFailed.set(true),
      });
  }

  totalMinutes(): number | null {
    const r = this.recipe();
    if (!r) return null;
    const total = (r.prepTimeMinutes ?? 0) + (r.cookTimeMinutes ?? 0);
    return total > 0 ? total : null;
  }

  open(): void {
    this.sheetRef.dismiss();
    if (typeof this.data.route === 'string') {
      this.router.navigate([this.data.route], { queryParams: this.data.queryParams ?? undefined });
    } else {
      this.router.navigate([...this.data.route], { queryParams: this.data.queryParams ?? undefined });
    }
  }

  close(): void {
    this.sheetRef.dismiss();
  }
}
