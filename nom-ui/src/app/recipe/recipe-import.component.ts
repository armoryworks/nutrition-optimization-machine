import { Component, computed, inject, signal, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
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
import { ScrapedIngredientModel, ScrapedRecipeModel } from '../core/models/scraped-recipe.model';
import { RecipeScrapingResponseModel } from '../core/models/recipe-scraping-response.model';
import { formatQuantity } from '../core/domain/shopping/unit-conversion';

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
  alreadyImported = signal<RecipeScrapingResponseModel | null>(null);
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
      error: (err: HttpErrorResponse) => {
        this.error.set(this.describeError(err, 'Failed to scrape recipe from URL. Make sure the URL points to a valid recipe page.'));
        this.previewLoading.set(false);
      },
    });
  }

  onImport(): void {
    if (!(this.url.value ?? '').trim() || this.importing() || this.importGated()) return;
    this.error.set('');
    this.alreadyImported.set(null);
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
        if (result.alreadyImported) {
          this.alreadyImported.set(result);
        } else if (result.success) {
          this.router.navigate(['/recipe', result.recipeId]);
        } else {
          this.error.set(result.error ?? 'Failed to import recipe.');
        }
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(this.describeError(err, 'Failed to import recipe.'));
        this.importing.set(false);
      },
    });
  }

  ingredientLine(ing: ScrapedIngredientModel): string {
    const main = [ing.quantity != null ? formatQuantity(ing.quantity) : '', ing.unit ?? '', ing.name].filter(Boolean).join(' ');
    return ing.notes ? `${main}, ${ing.notes}` : main;
  }

  formatDuration(value: string | null): string {
    const match = /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$/i.exec((value ?? '').trim());
    if (!match || !match.slice(1).some(Boolean)) return value ?? '';
    const days = Number(match[1] ?? 0);
    const hours = Number(match[2] ?? 0) + days * 24;
    const minutes = Number(match[3] ?? 0) + Math.round(Number(match[4] ?? 0) / 60);
    const parts = [hours ? `${hours} hr` : '', minutes ? `${minutes} min` : ''].filter(Boolean);
    return parts.length ? parts.join(' ') : '0 min';
  }

  private describeError(err: HttpErrorResponse, fallback: string): string {
    if (err.status !== 400) return fallback;
    if (!/^[a-z][a-z0-9+.-]*:\/\//i.test((this.url.value ?? '').trim())) {
      return 'That doesn\'t look like a full web address. Paste the recipe link including https:// at the start.';
    }
    const body = err.error;
    if (typeof body?.error === 'string' && body.error) return body.error;
    if (typeof body?.detail === 'string' && body.detail) return body.detail;
    const firstFieldError = body?.errors ? Object.values(body.errors as Record<string, string[]>).flat()[0] : undefined;
    return firstFieldError ?? fallback;
  }
}
