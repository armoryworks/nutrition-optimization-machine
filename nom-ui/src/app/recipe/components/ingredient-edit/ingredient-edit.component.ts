// File: nom-ui/src/app/recipe/components/ingredient-edit/ingredient-edit.component.ts

import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { Subject, of } from 'rxjs';
import { switchMap, takeUntil } from 'rxjs/operators';

import { RecipeService } from '../../services/recipe.service';
import { IngredientModel } from '../../models/ingredient.model';
import { IngredientFormComponent } from '../ingredient-form/ingredient-form.component';

@Component({
  selector: 'nom-ingredient-edit',
  standalone: true,
  imports: [
    CommonModule,
    IngredientFormComponent,
  ],
  templateUrl: './ingredient-edit.component.html',
  styleUrls: ['./ingredient-edit.component.scss']
})
export class IngredientEditComponent implements OnInit, OnDestroy {
  private ingredientService = inject(RecipeService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private location = inject(Location);
  private cdr = inject(ChangeDetectorRef);

  isLoading = false;
  isEditMode = false;
  ingredientId = 0;
  ingredient: IngredientModel | null = null;
  error: string | null = null;
  private destroy$ = new Subject<void>();

  // Back navigation properties
  private referringPage = '/recipes';
  private referringPageTitle = 'Recipes';

  // Explicit back button text property
  backButtonText = 'Back to Recipes';

  constructor() {
    // Constructor logic moved to unified component
  }

  ngOnInit(): void {
    // Debug: Log query parameters immediately
    console.log('ngOnInit - Query params:', this.route.snapshot.queryParams);

    // Capture referring page information immediately at the start
    this.captureReferringPage();

    this.route.paramMap.pipe(
      switchMap(params => {
        const id = params.get('id');
        if (id) {
          this.isEditMode = true;
          this.ingredientId = +id;
          this.isLoading = true;
          // Load the ingredient data when in edit mode
          return this.ingredientService.getIngredientDetails(this.ingredientId);
        } else {
          // Create mode
          this.isEditMode = false;
          this.ingredientId = 0;
          this.isLoading = false;
          return of(null);
        }
      }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: (ingredientData: IngredientModel | null) => {
        if (ingredientData) {
          this.ingredient = ingredientData;
          console.log('Loaded ingredient data:', ingredientData);
        }
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading ingredient:', error);
        this.error = 'Failed to load ingredient. Please try again.';
        this.isLoading = false;
      }
    });

    // Debug: Log the final referringPageTitle after all initialization logic
    console.log('ngOnInit - Final referringPageTitle:', this.referringPageTitle);
    console.log('ngOnInit - Final referringPage:', this.referringPage);
    console.log('ngOnInit - Final backButtonText:', this.backButtonText);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onFormSubmitted(ingredient: IngredientModel): void {
    // Navigate back to referring page after successful save
    this.navigateBack();
  }

  onFormCancelled(): void {
    // Navigate back to referring page when cancel is clicked
    this.navigateBack();
  }

  onBack(): void {
    // Navigate back to referring page when back button is clicked
    this.navigateBack();
  }

  onRefresh(): void {
    this.error = null;
    this.loadIngredient();
  }

  onRetry(): void {
    this.error = null;
    this.loadIngredient();
  }

  private captureReferringPage(): void {
    // Try to get referring page from query parameters first
    const returnTo = this.route.snapshot.queryParams['returnTo'];
    const returnToTitle = this.route.snapshot.queryParams['returnToTitle'];

    console.log('CaptureReferringPage - Query params:', { returnTo, returnToTitle });
    console.log('CaptureReferringPage - Before update - referringPageTitle:', this.referringPageTitle);

    if (returnTo) {
      this.referringPage = returnTo;
      this.referringPageTitle = returnToTitle || 'Previous Page';
      console.log('CaptureReferringPage - Using query params:', { returnTo, returnToTitle });
    } else {
      // Fallback: try to get from browser history or default to recipes
      const referrer = document.referrer;
      console.log('CaptureReferringPage - No query params, checking referrer:', referrer);
      if (referrer && referrer.includes(window.location.origin)) {
        // Extract path from referrer URL
        const url = new URL(referrer);
        console.log('CaptureReferringPage - Referrer URL:', url.pathname);
        if (url.pathname !== '/recipes/ingredients/new' && url.pathname !== '/recipes/ingredients/edit') {
          this.referringPage = url.pathname;
          this.referringPageTitle = this.getPageTitleFromPath(url.pathname);
          console.log('CaptureReferringPage - Using referrer:', { pathname: url.pathname, title: this.referringPageTitle });
        }
      } else {
        console.log('CaptureReferringPage - No valid referrer, using default');
      }
    }

    console.log('CaptureReferringPage - Final values:', {
      referringPage: this.referringPage,
      referringPageTitle: this.referringPageTitle
    });

    // Explicitly update the back button text property
    this.backButtonText = `Back to ${this.referringPageTitle}`;
    console.log('CaptureReferringPage - Updated backButtonText:', this.backButtonText);

    // Force change detection to ensure the UI updates
    this.cdr.detectChanges();

    console.log('CaptureReferringPage - After change detection - backButtonText value:', this.backButtonText);
  }

  private getPageTitleFromPath(path: string): string {
    // Map common paths to readable titles
    const pathTitles: Record<string, string> = {
      '/user/dashboard': 'Dashboard',
      '/recipes': 'Recipes',
      '/recipes/ingredients': 'Ingredients',
      '/user/recipe-author-dashboard': 'Recipe Author Dashboard',
      '/': 'Home'
    };

    return pathTitles[path] || 'Previous Page';
  }

  private navigateBack(): void {
    console.log('navigateBack called - referringPage:', this.referringPage);
    console.log('navigateBack called - current pathname:', window.location.pathname);

    // Try to navigate back to referring page, fallback to browser back
    if (this.referringPage && this.referringPage !== window.location.pathname) {
      console.log('navigateBack - navigating to:', this.referringPage);
      this.router.navigate([this.referringPage]);
    } else {
      console.log('navigateBack - using browser back navigation');
      // Fallback to browser back navigation
      this.location.back();
    }
  }

  private loadIngredient(): void {
    if (this.ingredientId) {
      this.isLoading = true;
      this.ingredientService.getIngredientDetails(this.ingredientId).pipe(
        takeUntil(this.destroy$)
      ).subscribe({
        next: (ingredientData: IngredientModel) => {
          this.ingredient = ingredientData;
          this.isLoading = false;
        },
        error: () => {
          console.error('Error loading ingredient');
          this.error = 'Failed to load ingredient. Please try again.';
          this.isLoading = false;
        }
      });
    }
  }
}