import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ReactiveFormsModule, NonNullableFormBuilder, Validators } from "@angular/forms";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatDividerModule } from "@angular/material/divider";
import { MatChipsModule } from "@angular/material/chips";
import { MatSnackBar } from "@angular/material/snack-bar";
import { Subject, takeUntil } from "rxjs";
import { RecipeCommentModel, RecipeCommentCreateModel } from "../../models/recipe-comment.model";
import { RecipeAdvancedService } from "../../services/recipe-advanced.service";
import { BaseListComponent, BaseListConfig } from "../../../common/components/base-list/base-list.component";

@Component({
    selector: "nom-recipe-comments",
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatDividerModule,
        MatChipsModule,
        BaseListComponent,
    ],
    templateUrl: "./recipe-comments.component.html",
    styleUrls: ["./recipe-comments.component.scss"],
})
export class RecipeCommentsComponent implements OnInit, OnDestroy {
    @Input() recipeId: number = 0;

    comments: RecipeCommentModel[] = [];
    commentForm = this.fb.group({
        commentText: ["", [Validators.required, Validators.maxLength(2047)]],
        title: ["", [Validators.maxLength(511)]],
    });

    isLoading = false;
    isSubmitting = false;
    error: string | null = null;
    private destroy$ = new Subject<void>();

    listConfig: BaseListConfig = {
        title: 'Recipe Comments',
        subtitle: 'Share your thoughts and read what others have to say',
        showSearch: false,
        showFilters: false,
        showPagination: false,
        maxWidth: '800px'
    };

    constructor(
        private fb: NonNullableFormBuilder,
        private recipeAdvancedService: RecipeAdvancedService,
        private snackBar: MatSnackBar
    ) { }

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
        this.recipeAdvancedService
            .getRecipeComments(this.recipeId)
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

        this.recipeAdvancedService
            .createComment(request)
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
        this.recipeAdvancedService
            .deleteComment(commentId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.comments = this.comments.filter(c => c.id !== commentId);
                    this.snackBar.open("Comment deleted successfully", "Close", { duration: 3000 });
                },
                error: (error) => {
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