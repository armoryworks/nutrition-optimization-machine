import { Component, OnInit, OnDestroy, inject, signal, input } from '@angular/core';
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
import { Subject, takeUntil } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeCommentModel, RecipeCommentCreateModel } from '../../models/recipe-comment.model';


@Component({
    selector: 'nom-recipe-comments',
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
    templateUrl: './recipe-comments.component.html',
    styleUrls: ['./recipe-comments.component.scss']
})
export class RecipeCommentsComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);
    private destroy$ = new Subject<void>();

    recipeId = input.required<number>();

    comments = signal<RecipeCommentModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    commentForm: FormGroup;
    isAddingComment = signal(false);
    isSubmitting = signal(false);



    constructor() {
        this.commentForm = this.nonNullableFb.group({
            title: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
            commentText: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(2047)]]
        });
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadComments();
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    loadComments(): void {
        this.isLoading.set(true);
        this.error.set(null);
        this.recipeService
            .getComments(this.recipeId())
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (comments) => {
                    this.comments.set(comments);
                    this.isLoading.set(false);
                },
                error: (error) => {
                    console.error("Error loading comments:", error);
                    this.error.set("Failed to load comments. Please try again.");
                    this.isLoading.set(false);
                },
            });
    }

    submitComment(): void {
        if (this.commentForm.invalid || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        const request: RecipeCommentCreateModel = {
            recipeId: this.recipeId(),
            commentText: this.commentForm.get("commentText")!.value,
            title: this.commentForm.get("title")!.value || undefined,
        };

        this.recipeService
            .addComment(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (comment) => {
                    this.comments.set([comment, ...this.comments()]);
                    this.commentForm.reset();
                    this.snackBar.open("Comment added successfully", "Close", { duration: 3000 });
                    this.isSubmitting.set(false);
                },
                error: (error) => {
                    console.error("Error creating comment:", error);
                    this.error.set("Failed to post comment. Please try again.");
                    this.isSubmitting.set(false);
                },
            });
    }

    deleteComment(commentId: number): void {
        this.recipeService
            .deleteComment(commentId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.comments.set(this.comments().filter(c => c.id !== commentId));
                    this.snackBar.open("Comment deleted successfully", "Close", { duration: 3000 });
                },
                error: (error: any) => {
                    console.error("Error deleting comment:", error);
                    this.snackBar.open("Failed to delete comment", "Close", { duration: 3000 });
                },
            });
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