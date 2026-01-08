import { Component, OnInit, inject, signal, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { RecipeService } from '../../services/recipe.service';
import { RecipeRatingModel, RecipeRatingResponseModel } from '../../models/recipe-rating.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';

@Component({
    selector: 'nom-recipe-ratings',
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
        MatChipsModule,
        MatDividerModule,
        MatDialogModule,
        MatListModule,
        MatMenuModule,
    ],
    templateUrl: './recipe-ratings.component.html',
    styleUrls: ['./recipe-ratings.component.scss']
})
export class RecipeRatingsComponent implements OnInit {
    private recipeService = inject(RecipeService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);
    private userInfoService = inject(UserInfoService);

    recipeId = input.required<number>();

    ratings = signal<RecipeRatingResponseModel[]>([]);
    userRating = signal<RecipeRatingModel | null>(null);
    averageRating = signal(0);
    isLoading = signal(false);
    isSubmitting = signal(false);
    ratingForm: FormGroup = this.nonNullableFb.group({
        rating: [0, [Validators.required, Validators.min(1), Validators.max(5)]],
        comment: ['', [Validators.maxLength(1000)]]
    });

    constructor() {
        // Form is now initialized at declaration
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadRatings();
        }
    }

    loadRatings(): void {
        this.isLoading.set(true);
        this.recipeService.getRatings(this.recipeId()).subscribe({
            next: (ratings) => {
                this.ratings.set(ratings);
                this.calculateAverageRating();
                this.findUserRating();
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading ratings:', error);
                this.snackBar.open('Failed to load ratings', 'Close', {
                    duration: 3000,
                    horizontalPosition: 'center',
                    verticalPosition: 'top'
                });
                this.isLoading.set(false);
            }
        });
    }

    calculateAverageRating(): void {
        if (this.ratings().length === 0) {
            this.averageRating.set(0);
            return;
        }

        const totalRating = this.ratings().reduce((sum, rating) => sum + rating.rating, 0);
        this.averageRating.set(totalRating / this.ratings().length);
    }

    findUserRating(): void {
        const currentPersonId = this.userInfoService.getCurrentUserInfoValue()?.personId;
        if (currentPersonId) {
            this.userRating.set(this.ratings().find(r => r.authorId === currentPersonId) || null);
        }

        if (this.userRating()) {
            this.ratingForm.patchValue({
                rating: this.userRating()!.rating,
                comment: this.userRating()!.comment || ''
            });
        }
    }

    onSubmit(): void {
        if (this.ratingForm.valid && !this.isSubmitting()) {
            this.isSubmitting.set(true);

            const currentPersonId = this.userInfoService.getCurrentUserInfoValue()?.personId;
            if (!currentPersonId) {
                this.snackBar.open('User information not available. Please log in again.', 'Close', {
                    duration: 3000,
                    horizontalPosition: 'center',
                    verticalPosition: 'top'
                });
                return;
            }

            const ratingData = {
                recipeId: this.recipeId(),
                rating: this.ratingForm.value.rating,
                comment: this.ratingForm.value.comment
            };

            if (this.userRating()) {
                // Update existing rating
                this.recipeService.updateRating(this.userRating()!.id, ratingData).subscribe({
                    next: (updatedRating) => {
                        const index = this.ratings().findIndex(r => r.id === updatedRating.id);
                        if (index !== -1) {
                            const updatedRatings = [...this.ratings()];
                            updatedRatings[index] = updatedRating;
                            this.ratings.set(updatedRatings);
                        }
                        this.userRating.set(updatedRating);
                        this.calculateAverageRating();
                        this.isSubmitting.set(false);
                        this.snackBar.open('Rating updated successfully!', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                    },
                    error: (error) => {
                        console.error('Error updating rating:', error);
                        this.snackBar.open('Failed to update rating', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.isSubmitting.set(false);
                    }
                });
            } else {
                // Add new rating
                this.recipeService.addRating(ratingData).subscribe({
                    next: (newRating) => {
                        this.ratings.set([newRating, ...this.ratings()]);
                        this.userRating.set(newRating);
                        this.calculateAverageRating();
                        this.isSubmitting.set(false);
                        this.snackBar.open('Rating added successfully!', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                    },
                    error: (error) => {
                        console.error('Error adding rating:', error);
                        this.snackBar.open('Failed to add rating', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.isSubmitting.set(false);
                    }
                });
            }
        }
    }

    onDeleteRating(): void {
        if (this.userRating() && confirm('Are you sure you want to delete your rating?')) {
            this.recipeService.deleteRating(this.userRating()!.id).subscribe({
                next: () => {
                    this.ratings.set(this.ratings().filter(r => r.id !== this.userRating()!.id));
                    this.userRating.set(null);
                    this.calculateAverageRating();
                    this.ratingForm.reset();
                    this.snackBar.open('Rating deleted successfully!', 'Close', {
                        duration: 3000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                },
                error: (error) => {
                    console.error('Error deleting rating:', error);
                    this.snackBar.open('Failed to delete rating', 'Close', {
                        duration: 3000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                }
            });
        }
    }

    canDeleteRating(rating: RecipeRatingModel): boolean {
        const currentUser = this.userInfoService.getCurrentUserInfoValue();
        if (!currentUser) return false;

        // Check if current user is the author or has admin rights
        return rating.authorId === currentUser.personId || currentUser.isAdmin;
    }

    getStarRating(rating: number): string[] {
        const stars = [];
        for (let i = 1; i <= 5; i++) {
            stars.push(i <= rating ? 'star' : 'star_border');
        }
        return stars;
    }

    formatDate(date: Date): string {
        return new Date(date).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    }
} 