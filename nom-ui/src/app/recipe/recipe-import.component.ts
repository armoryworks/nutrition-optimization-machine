import { Component, computed, inject, signal, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RecipeScrapingService } from '../core/services/recipe-scraping.service';
import { LoadingService } from '../core/services/loading.service';
import { PolicyService } from '../core/services/policy.service';
import { ScrapedRecipeModel } from '../core/models/scraped-recipe.model';

@Component({
  selector: 'nom-recipe-import',
  imports: [ReactiveFormsModule, RouterLink, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './recipe-import.component.html',
  styleUrl: './recipe-import.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeImport {
  private scrapingService = inject(RecipeScrapingService);
  private loadingService = inject(LoadingService);
  private policyService = inject(PolicyService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  constructor() {
    this.policyService.loadOwnPolicyForPrimaryHousehold();
  }

  importGated = computed(() => this.policyService.isGatedPrimary('recipe_import'));

  url = new FormControl('');
  importKeywordsAsTags = new FormControl(true);
  preview = signal<ScrapedRecipeModel | null>(null);
  importing = signal(false);
  error = signal('');
  previewLoading = signal(false);

  onPreview(): void {
    if (!(this.url.value ?? '').trim()) return;
    this.error.set('');
    this.previewLoading.set(true);
    this.preview.set(null);

    this.scrapingService.testScrape({ url: this.url.value ?? '' }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
    if (!(this.url.value ?? '').trim() || this.importing() || this.importGated()) return;
    this.error.set('');
    this.importing.set(true);

    this.scrapingService.importFromUrl({
      url: this.url.value ?? '',
      importKeywordsAsTags: this.importKeywordsAsTags.value ?? true,
    }).pipe(
      this.loadingService.loading('Importing recipe...'),
      takeUntilDestroyed(this.destroyRef),
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
