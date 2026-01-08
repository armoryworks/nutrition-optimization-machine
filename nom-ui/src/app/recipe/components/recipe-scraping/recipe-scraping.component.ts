import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatCheckboxModule } from '@angular/material/checkbox';

import { RecipeService } from '../../services/recipe.service';
import { RecipeScrapingModel, RecipeScrapingRequestModel } from '../../models/recipe-scraping.model';

@Component({
    selector: 'nom-recipe-scraping',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatCheckboxModule,
    ],
    templateUrl: './recipe-scraping.component.html',
    styleUrls: ['./recipe-scraping.component.scss']
})
export class RecipeScrapingComponent {
    private recipeService = inject(RecipeService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);

    scrapingForm: FormGroup;
    isLoading = signal(false);
    isScraping = signal(false);
    error = signal<string | null>(null);
    scrapedRecipe = signal<RecipeScrapingModel | null>(null);
    showPreview = signal(false);

    constructor() {
        this.scrapingForm = this.nonNullableFb.group({
            url: ['', [Validators.required, Validators.pattern('https?://.+')]],
            importKeywordsAsTags: [true],
            stayInEditMode: [false]
        });
    }



    /**
     * Test scrape recipe from URL
     */
    async testScrape(): Promise<void> {
        if (this.scrapingForm.invalid) {
            return;
        }

        this.isLoading.set(true);
        this.error.set(null);
        this.scrapedRecipe.set(null);

        try {
            const request = {
                url: this.scrapingForm.get('url')?.value,
                useOpenAI: false
            };

            const result = await this.recipeService.testScrapeRecipe(request).toPromise();
            this.scrapedRecipe.set(result || null);
            this.showPreview.set(true);
        } catch (error: unknown) {
            this.error.set((error as { error?: { message?: string } })?.error?.message || 'Failed to test scrape recipe');
            console.error('Error testing recipe scraping:', error);
        } finally {
            this.isLoading.set(false);
        }
    }

    /**
     * Create recipe from scraped data
     */
    async createRecipe(): Promise<void> {
        if (!this.scrapedRecipe()) {
            return;
        }

        this.isLoading.set(true);
        this.error.set(null);

        try {
            const request: RecipeScrapingRequestModel = {
                url: this.scrapingForm.get('url')?.value,
                importKeywordsAsTags: this.scrapingForm.get('importKeywordsAsTags')?.value,
                stayInEditMode: this.scrapingForm.get('stayInEditMode')?.value
            };

            const response = await this.recipeService.scrapeRecipeFromUrl(request).toPromise();

            if (response && response.success) {
                // Navigate to the created recipe
                this.router.navigate(['/recipes', response.recipeId]);
            } else {
                this.error.set((response && response.error) || 'Failed to create recipe');
            }
        } catch (error: unknown) {
            this.error.set((error as { error?: { message?: string } })?.error?.message || 'Failed to create recipe');
            console.error('Error creating recipe:', error);
        } finally {
            this.isLoading.set(false);
        }
    }

    /**
     * Cancel scraping and go back
     */
    cancel(): void {
        this.router.navigate(['/recipes']);
    }

    /**
     * Clear form and reset state
     */
    reset(): void {
        this.scrapingForm.reset();
        this.error.set(null);
        this.scrapedRecipe.set(null);
        this.showPreview.set(false);
    }

    /**
     * Get form control for validation
     */
    get urlControl() {
        return this.scrapingForm.get('url');
    }

    /**
     * Check if URL is valid
     */
    get isUrlValid(): boolean {
        return this.urlControl?.valid || false;
    }
} 