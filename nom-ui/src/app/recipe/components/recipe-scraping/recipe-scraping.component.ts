import { Component, OnInit } from '@angular/core';
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
import { RecipeScrapingModel, RecipeScrapingRequestModel, RecipeScrapingResponseModel } from '../../models/recipe-scraping.model';

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
export class RecipeScrapingComponent implements OnInit {
    scrapingForm: FormGroup;
    isLoading = false;
    isScraping = false;
    error: string | null = null;
    scrapedRecipe: RecipeScrapingModel | null = null;
    showPreview = false;

    constructor(
        private recipeService: RecipeService,
        private router: Router,
        private nonNullableFb: NonNullableFormBuilder,
        private snackBar: MatSnackBar
    ) {
        this.scrapingForm = this.nonNullableFb.group({
            url: ['', [Validators.required, Validators.pattern('https?://.+')]],
            importKeywordsAsTags: [true],
            stayInEditMode: [false]
        });
    }

    ngOnInit(): void {
        // Component initialization
    }

    /**
     * Test scrape recipe from URL
     */
    async testScrape(): Promise<void> {
        if (this.scrapingForm.invalid) {
            return;
        }

        this.isLoading = true;
        this.error = null;
        this.scrapedRecipe = null;

        try {
            const request = {
                url: this.scrapingForm.get('url')?.value,
                useOpenAI: false
            };

            this.scrapedRecipe = await this.recipeService.testScrapeRecipe(request).toPromise();
            this.showPreview = true;
        } catch (error: any) {
            this.error = error.error?.message || 'Failed to test scrape recipe';
            console.error('Error testing recipe scraping:', error);
        } finally {
            this.isLoading = false;
        }
    }

    /**
     * Create recipe from scraped data
     */
    async createRecipe(): Promise<void> {
        if (!this.scrapedRecipe) {
            return;
        }

        this.isLoading = true;
        this.error = null;

        try {
            const request: RecipeScrapingRequestModel = {
                url: this.scrapingForm.get('url')?.value,
                importKeywordsAsTags: this.scrapingForm.get('importKeywordsAsTags')?.value,
                stayInEditMode: this.scrapingForm.get('stayInEditMode')?.value
            };

            const response = await this.recipeService.scrapeRecipeFromUrl(request).toPromise();

            if (response.success) {
                // Navigate to the created recipe
                this.router.navigate(['/recipe', response.recipeId]);
            } else {
                this.error = response.error || 'Failed to create recipe';
            }
        } catch (error: any) {
            this.error = error.error?.message || 'Failed to create recipe';
            console.error('Error creating recipe:', error);
        } finally {
            this.isLoading = false;
        }
    }

    /**
     * Cancel scraping and go back
     */
    cancel(): void {
        this.router.navigate(['/recipe']);
    }

    /**
     * Clear form and reset state
     */
    reset(): void {
        this.scrapingForm.reset();
        this.error = null;
        this.scrapedRecipe = null;
        this.showPreview = false;
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