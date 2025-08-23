import { Component, OnInit, OnDestroy, inject, Input } from '@angular/core';
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
import { RecipeCommentModel, RecipeCommentCreateModel } from '../../models/recipe-comment.model';
import { Subject, takeUntil } from 'rxjs';


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

    @Input() recipeId!: number;

    comments: RecipeCommentModel[] = [];
    isLoading = false;
    error: string | null = null;
    commentForm: FormGroup;
    isAddingComment = false;
    isSubmitting = false;
    private destroy$ = new Subject<void>();



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
        this.isLoading = true;
        this.error = null;
        this.recipeService
            .getComments(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (comments) => {
                    this.comments = comments;
                    this.isLoading = false;
                },
                error: (error) => {
                    console.error("Error loading comments:", error);
                    this.error = "Failed to load comments. Please try again.";
                    this.isLoading = false;
                },
            });
    }

    submitComment(): void {
        if (this.commentForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;
        const request: RecipeCommentCreateModel = {
            recipeId: this.recipeId,
            commentText: this.commentForm.get("commentText")!.value,
            title: this.commentForm.get("title")!.value || undefined,
        };

        this.recipeService
            .addComment(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (comment) => {
                    this.comments.unshift(comment);
                    this.commentForm.reset();
                    this.snackBar.open("Comment added successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating comment:", error);
                    this.error = "Failed to post comment. Please try again.";
                    this.isSubmitting = false;
                },
            });
    }

    deleteComment(commentId: number): void {
        this.recipeService
            .deleteComment(commentId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.comments = this.comments.filter(c => c.id !== commentId);
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