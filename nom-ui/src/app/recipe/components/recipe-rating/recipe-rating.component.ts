import { Component, OnInit, OnDestroy, inject, signal, input } from '@angular/core';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NotificationService } from '../../../utilities/services/notification.service';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwTextareaComponent, AmwCardComponent, AmwIconComponent, AmwInlineLoadingComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { RecipeService } from '../../services/recipe.service';
import { RecipeRatingResponseModel } from '../../models/recipe-rating.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';


@Component({
    selector: 'nom-recipe-rating',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwTextareaComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
        AmwValidationTooltipDirective,
    ],
    templateUrl: './recipe-rating.component.html',
    styleUrls: ['./recipe-rating.component.scss']
})
export class RecipeRatingComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
    private validationService = inject(AmwValidationService);
    private destroy$ = new Subject<void>();

    validationContext!: ValidationContext;
    recipeId = input<number>();

    rating = signal<RecipeRatingResponseModel | null>(null);
    userRating = signal<RecipeRatingResponseModel | null>(null);
    averageRating = signal<number>(0);
    totalRatings = signal<number>(0);
    isLoading = signal(false);
    error = signal<string | null>(null);
    ratingForm: FormGroup;
    isSubmittingRating = signal(false);
    isSubmitting = signal(false);



    constructor() {
        this.ratingForm = this.nonNullableFb.group({
            rating: [0, [Validators.required, Validators.min(1), Validators.max(5)]],
            reviewText: ['', [Validators.maxLength(1000)]]
        });
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadRatingData();
        }

        // Setup ValidationContext
        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Rating validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'rating-required',
            message: 'Rating is required',
            severity: 'error',
            field: 'rating',
            control: this.ratingForm.get('rating') ?? undefined,
            validator: () => !this.ratingForm.get('rating')?.hasError('required')
        });

        // Rating validation - min
        this.validationService.addViolation(this.validationContext.id, {
            id: 'rating-min',
            message: 'Rating must be at least 1',
            severity: 'error',
            field: 'rating',
            control: this.ratingForm.get('rating') ?? undefined,
            validator: () => !this.ratingForm.get('rating')?.hasError('min')
        });

        // Rating validation - max
        this.validationService.addViolation(this.validationContext.id, {
            id: 'rating-max',
            message: 'Rating must be at most 5',
            severity: 'error',
            field: 'rating',
            control: this.ratingForm.get('rating') ?? undefined,
            validator: () => !this.ratingForm.get('rating')?.hasError('max')
        });

        // Review text validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'reviewText-maxlength',
            message: 'Review text must be 1000 characters or less',
            severity: 'error',
            field: 'reviewText',
            control: this.ratingForm.get('reviewText') ?? undefined,
            validator: () => !this.ratingForm.get('reviewText')?.hasError('maxlength')
        });
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();

        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    loadRatingData(): void {
        this.isLoading.set(true);
        this.error.set(null);

        // Load user rating and average rating in parallel
        const userRating$ = this.recipeService.getUserRating(this.recipeId()!);
        const averageRating$ = this.recipeService.getRecipeAverageRating(this.recipeId()!);

        userRating$.pipe(takeUntil(this.destroy$)).subscribe({
            next: (rating) => {
                this.userRating.set(rating);
                if (rating) {
                    this.ratingForm.patchValue({
                        rating: rating.rating,
                        reviewText: rating.reviewText || "",
                    });
                }
            },
            error: () => {
                // User hasn't rated yet, which is fine
                console.log("User has not rated this recipe yet");
            },
        });

        averageRating$.pipe(takeUntil(this.destroy$)).subscribe({
            next: (response) => {
                this.averageRating.set(response.averageRating);
                this.totalRatings.set(response.totalRatings);
                this.isLoading.set(false);
            },
            error: () => {
                console.error("Error loading average rating");
                this.error.set(ERROR_MESSAGES.RECIPE.LOAD_FAILED);
                this.isLoading.set(false);
            },
        });
    }

    submitRating(): void {
        if (this.ratingForm.invalid || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        const request: any = {
            recipeId: this.recipeId(),
            rating: this.ratingForm.get("rating")!.value,
            reviewText: this.ratingForm.get("reviewText")!.value || undefined,
        };

        this.recipeService
            .createRating(request)
            .pipe(
                loading('Submitting rating...'),
                takeUntil(this.destroy$)
            )
            .subscribe({
                next: (rating) => {
                    this.userRating.set(rating);
                    this.notificationService.success("Rating submitted successfully");
                    this.isSubmitting.set(false);
                    // Reload average rating
                    this.loadRatingData();
                },
                error: () => {
                    console.error("Error submitting rating");
                    this.error.set(ERROR_MESSAGES.RECIPE.SAVE_FAILED);
                    this.isSubmitting.set(false);
                },
            });
    }

    updateRating(): void {
        if (this.ratingForm.invalid || this.isSubmitting() || !this.userRating()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        const request: any = {
            recipeId: this.recipeId(),
            rating: this.ratingForm.get("rating")!.value,
            reviewText: this.ratingForm.get("reviewText")!.value || undefined,
        };

        this.recipeService
            .updateRating(this.userRating()!.id, request)
            .pipe(
                loading('Updating rating...'),
                takeUntil(this.destroy$)
            )
            .subscribe({
                next: (rating) => {
                    this.userRating.set(rating);
                    this.notificationService.success("Rating updated successfully");
                    this.isSubmitting.set(false);
                    // Reload average rating
                    this.loadRatingData();
                },
                error: () => {
                    console.error("Error updating rating");
                    this.error.set(ERROR_MESSAGES.RECIPE.SAVE_FAILED);
                    this.isSubmitting.set(false);
                },
            });
    }

    deleteRating(): void {
        if (!this.userRating() || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        this.recipeService
            .deleteRating(this.userRating()!.id)
            .pipe(
                loading('Deleting rating...'),
                takeUntil(this.destroy$)
            )
            .subscribe({
                next: () => {
                    this.userRating.set(null);
                    this.ratingForm.reset();
                    this.notificationService.success("Rating deleted successfully");
                    this.isSubmitting.set(false);
                    // Reload average rating
                    this.loadRatingData();
                },
                error: () => {
                    console.error("Error deleting rating");
                    this.error.set(ERROR_MESSAGES.RECIPE.DELETE_FAILED);
                    this.isSubmitting.set(false);
                },
            });
    }

    setRating(rating: number): void {
        this.ratingForm.patchValue({ rating });
    }

    getStarClass(rating: number): string {
        const currentRating = this.ratingForm.get("rating")?.value || 0;
        return rating <= currentRating ? "nom-recipe-rating__star--filled" : "nom-recipe-rating__star--empty";
    }

    getAverageStarClass(rating: number): string {
        return rating <= this.averageRating() ? "nom-recipe-rating__star--filled" : "nom-recipe-rating__star--empty";
    }

    formatDate(dateString: string): string {
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }
} 