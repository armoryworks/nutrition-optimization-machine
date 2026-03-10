import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RecipeScrapingService } from '../core/services/recipe-scraping.service';
import { LoadingService } from '../core/services/loading.service';
import { ScrapedRecipeModel } from '../core/models/scraped-recipe.model';

@Component({
  selector: 'nom-recipe-import',
  imports: [FormsModule, RouterLink, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatProgressSpinnerModule],
  templateUrl: './recipe-import.component.html',
  styleUrl: './recipe-import.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeImport {
  private scrapingService = inject(RecipeScrapingService);
  private loadingService = inject(LoadingService);
  private router = inject(Router);

  url = '';
  importKeywordsAsTags = true;
  preview = signal<ScrapedRecipeModel | null>(null);
  importing = signal(false);
  error = signal('');
  previewLoading = signal(false);

  onPreview(): void {
    if (!this.url.trim()) return;
    this.error.set('');
    this.previewLoading.set(true);
    this.preview.set(null);

    this.scrapingService.testScrape({ url: this.url }).subscribe({
      next: (result) => {
        this.preview.set(result);
        this.previewLoading.set(false);
      },
      error: () => {
        this.error.set('Failed to scrape recipe from URL. Make sure the URL points to a valid recipe page.');
        this.previewLoading.set(false);
      },
    });
  }

  onImport(): void {
    if (!this.url.trim() || this.importing()) return;
    this.error.set('');
    this.importing.set(true);

    this.scrapingService.importFromUrl({
      url: this.url,
      importKeywordsAsTags: this.importKeywordsAsTags,
    }).pipe(
      this.loadingService.loading('Importing recipe...'),
    ).subscribe({
      next: (result) => {
        this.importing.set(false);
        if (result.success) {
          this.router.navigate(['/recipe', result.recipeId]);
        } else {
          this.error.set(result.error ?? 'Failed to import recipe.');
        }
      },
      error: () => {
        this.error.set('Failed to import recipe.');
        this.importing.set(false);
      },
    });
  }
}
