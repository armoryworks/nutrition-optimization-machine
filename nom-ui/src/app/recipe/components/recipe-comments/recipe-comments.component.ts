import { Component, OnInit, OnDestroy, inject, signal, input } from '@angular/core';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NotificationService } from '../../../utilities/services/notification.service';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwInputComponent, AmwTextareaComponent, AmwIconComponent, AmwIconButtonComponent } from 'angular-material-wrap';

import { RecipeService } from '../../services/recipe.service';
import { RecipeCommentModel, RecipeCommentCreateModel } from '../../models/recipe-comment.model';


@Component({
    selector: 'nom-recipe-comments',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwIconComponent,
        AmwIconButtonComponent,
    ],
    templateUrl: './recipe-comments.component.html',
    styleUrls: ['./recipe-comments.component.scss']
})
export class RecipeCommentsComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
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
                    this.notificationService.success("Comment added successfully");
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
                    this.notificationService.success("Comment deleted successfully");
                },
                error: (error: any) => {
                    console.error("Error deleting comment:", error);
                    this.notificationService.error("Failed to delete comment");
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