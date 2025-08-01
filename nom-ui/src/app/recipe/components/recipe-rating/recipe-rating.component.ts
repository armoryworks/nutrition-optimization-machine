import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ReactiveFormsModule, NonNullableFormBuilder, Validators } from "@angular/forms";
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatDividerModule } from "@angular/material/divider";
import { MatChipsModule } from "@angular/material/chips";
import { MatSnackBar } from "@angular/material/snack-bar";
import { MatTooltipModule } from "@angular/material/tooltip";
import { Subject, takeUntil } from "rxjs";
import { RecipeRatingModel, RecipeRatingCreateModel } from "../../models/recipe-rating.model";
import { RecipeAdvancedService } from "../../services/recipe-advanced.service";

@Component({
    selector: "app-recipe-rating",
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatDividerModule,
        MatChipsModule,
        MatTooltipModule,
    ],
    templateUrl: "./recipe-rating.component.html",
    styleUrls: ["./recipe-rating.component.scss"],
})
export class RecipeRatingComponent implements OnInit, OnDestroy {
    @Input() recipeId: number = 0;

    userRating: RecipeRatingModel | null = null;
    averageRating: number = 0;
    totalRatings: number = 0;
    ratingForm = this.fb.group({
        rating: [0, [Validators.required, Validators.min(1), Validators.max(5)]],
        reviewText: ["", [Validators.maxLength(2047)]],
    });

    isLoading = false;
    isSubmitting = false;
    private destroy$ = new Subject<void>();

    constructor(
        private fb: NonNullableFormBuilder,
        private recipeAdvancedService: RecipeAdvancedService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadRatingData();
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    loadRatingData(): void {
        this.isLoading = true;

        // Load user rating and average rating in parallel
        const userRating$ = this.recipeAdvancedService.getUserRating(this.recipeId);
        const averageRating$ = this.recipeAdvancedService.getRecipeAverageRating(this.recipeId);

        userRating$.pipe(takeUntil(this.destroy$)).subscribe({
            next: (rating) => {
                this.userRating = rating;
                if (rating) {
                    this.ratingForm.patchValue({
                        rating: rating.rating,
                        reviewText: rating.reviewText || "",
                    });
                }
            },
            error: (error) => {
                // User hasn't rated yet, which is fine
                console.log("User has not rated this recipe yet");
            },
        });

        averageRating$.pipe(takeUntil(this.destroy$)).subscribe({
            next: (response) => {
                this.averageRating = response.averageRating;
                this.isLoading = false;
            },
            error: (error) => {
                console.error("Error loading average rating:", error);
                this.isLoading = false;
            },
        });
    }

    submitRating(): void {
        if (this.ratingForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        const request: RecipeRatingCreateModel = {
            recipeId: this.recipeId,
            rating: this.ratingForm.get("rating")!.value,
            reviewText: this.ratingForm.get("reviewText")!.value || undefined,
        };

        this.recipeAdvancedService
            .createRating(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (rating) => {
                    this.userRating = rating;
                    this.loadRatingData(); // Reload average rating
                    this.snackBar.open("Rating submitted successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating rating:", error);
                    this.snackBar.open("Failed to submit rating", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
            });
    }

    updateRating(): void {
        if (this.ratingForm.invalid || this.isSubmitting || !this.userRating) {
            return;
        }

        this.isSubmitting = true;
        const request: RecipeRatingCreateModel = {
            recipeId: this.recipeId,
            rating: this.ratingForm.get("rating")!.value,
            reviewText: this.ratingForm.get("reviewText")!.value || undefined,
        };

        this.recipeAdvancedService
            .updateRating(this.userRating.id, request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.userRating = {
                        ...this.userRating!,
                        rating: request.rating,
                        reviewText: request.reviewText,
                    };
                    this.loadRatingData(); // Reload average rating
                    this.snackBar.open("Rating updated successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error updating rating:", error);
                    this.snackBar.open("Failed to update rating", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
            });
    }

    deleteRating(): void {
        if (!this.userRating || this.isSubmitting) {
            return;
        }

        if (confirm("Are you sure you want to delete your rating?")) {
            this.isSubmitting = true;
            this.recipeAdvancedService
                .deleteRating(this.userRating.id)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: () => {
                        this.userRating = null;
                        this.ratingForm.reset();
                        this.loadRatingData(); // Reload average rating
                        this.snackBar.open("Rating deleted successfully", "Close", { duration: 3000 });
                        this.isSubmitting = false;
                    },
                    error: (error) => {
                        console.error("Error deleting rating:", error);
                        this.snackBar.open("Failed to delete rating", "Close", { duration: 3000 });
                        this.isSubmitting = false;
                    },
                });
        }
    }

    setRating(rating: number): void {
        this.ratingForm.patchValue({ rating });
    }

    getStarClass(rating: number): string {
        const currentRating = this.ratingForm.get("rating")?.value || 0;
        if (rating <= currentRating) {
            return "star-filled";
        }
        return "star-empty";
    }

    getAverageStarClass(rating: number): string {
        if (rating <= this.averageRating) {
            return "star-filled";
        }
        return "star-empty";
    }

    formatDate(dateString: string): string {
        return new Date(dateString).toLocaleDateString("en-US", {
            year: "numeric",
            month: "short",
            day: "numeric",
        });
    }
} 