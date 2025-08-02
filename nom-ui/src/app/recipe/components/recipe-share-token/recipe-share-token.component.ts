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
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatSelectModule } from "@angular/material/select";
import { Subject, takeUntil } from "rxjs";

import { RecipeAdvancedService } from "../../services/recipe-advanced.service";
import { RecipeShareTokenModel, RecipeShareTokenCreateModel } from "../../models/recipe-share-token.model";
import { BaseDetailComponent, BaseDetailConfig } from "../../../common/components/base-detail/base-detail.component";

@Component({
    selector: "app-recipe-share-token",
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
        MatTooltipModule,
        MatSelectModule,
        BaseDetailComponent,
    ],
    templateUrl: "./recipe-share-token.component.html",
    styleUrls: ["./recipe-share-token.component.scss"],
})
export class RecipeShareTokenComponent implements OnInit, OnDestroy {
    @Input() recipeId: number = 0;

    shareTokens: RecipeShareTokenModel[] = [];
    shareTokenForm = this.fb.group({
        shareName: ["", [Validators.maxLength(511)]],
        isPublic: [false],
    });

    isLoading = false;
    isSubmitting = false;
    error: string | null = null;
    private destroy$ = new Subject<void>();

    detailConfig: BaseDetailConfig = {
        title: 'Share Recipe',
        subtitle: 'Create share tokens to allow others to view this recipe',
        showBackButton: false,
        maxWidth: '800px'
    };

    constructor(
        private fb: NonNullableFormBuilder,
        private recipeAdvancedService: RecipeAdvancedService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadShareTokens();
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    loadShareTokens(): void {
        this.isLoading = true;
        this.error = null;
        this.recipeAdvancedService
            .getRecipeShareTokens(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (shareTokens) => {
                    this.shareTokens = shareTokens;
                    this.isLoading = false;
                },
                error: (error) => {
                    console.error("Error loading share tokens:", error);
                    this.error = "Failed to load share tokens. Please try again.";
                    this.isLoading = false;
                },
            });
    }

    createShareToken(): void {
        if (this.shareTokenForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;
        const request: RecipeShareTokenCreateModel = {
            recipeId: this.recipeId,
            shareName: this.shareTokenForm.get("shareName")!.value || undefined,
            isPublic: this.shareTokenForm.get("isPublic")!.value,
        };

        this.recipeAdvancedService
            .createShareToken(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (shareToken) => {
                    this.shareTokens.unshift(shareToken);
                    this.shareTokenForm.reset();
                    this.snackBar.open("Share token created successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating share token:", error);
                    this.error = "Failed to create share token. Please try again.";
                    this.isSubmitting = false;
                },
            });
    }

    deleteShareToken(shareTokenId: number): void {
        this.recipeAdvancedService
            .deleteShareToken(shareTokenId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.shareTokens = this.shareTokens.filter(t => t.id !== shareTokenId);
                    this.snackBar.open("Share token deleted successfully", "Close", { duration: 3000 });
                },
                error: (error) => {
                    console.error("Error deleting share token:", error);
                    this.snackBar.open("Failed to delete share token", "Close", { duration: 3000 });
                },
            });
    }

    copyShareToken(shareToken: string): void {
        navigator.clipboard.writeText(this.getShareUrl(shareToken)).then(() => {
            this.snackBar.open("Share URL copied to clipboard", "Close", { duration: 3000 });
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

    getShareUrl(shareToken: string): string {
        return `${window.location.origin}/recipe/shared/${shareToken}`;
    }
} 