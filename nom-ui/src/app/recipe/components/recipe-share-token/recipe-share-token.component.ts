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

import { RecipeAdvancedService } from "../../services/recipe-advanced.service";
import { RecipeShareTokenModel, RecipeShareTokenCreateModel } from "../../models/recipe-share-token.model";

@Component({
    selector: "app-recipe-share-token",
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
    private destroy$ = new Subject<void>();

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
                    this.snackBar.open("Failed to load share tokens", "Close", { duration: 3000 });
                    this.isLoading = false;
                },
            });
    }

    createShareToken(): void {
        if (this.shareTokenForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
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
                    this.shareTokenForm.reset({ isPublic: false });
                    this.snackBar.open("Share token created successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating share token:", error);
                    this.snackBar.open("Failed to create share token", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
            });
    }

    deleteShareToken(shareTokenId: number): void {
        if (confirm("Are you sure you want to delete this share token?")) {
            this.recipeAdvancedService
                .deleteShareToken(shareTokenId)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: () => {
                        this.shareTokens = this.shareTokens.filter((st) => st.id !== shareTokenId);
                        this.snackBar.open("Share token deleted successfully", "Close", { duration: 3000 });
                    },
                    error: (error) => {
                        console.error("Error deleting share token:", error);
                        this.snackBar.open("Failed to delete share token", "Close", { duration: 3000 });
                    },
                });
        }
    }

    copyShareToken(shareToken: string): void {
        navigator.clipboard.writeText(shareToken).then(() => {
            this.snackBar.open("Share token copied to clipboard", "Close", { duration: 3000 });
        }).catch(() => {
            this.snackBar.open("Failed to copy share token", "Close", { duration: 3000 });
        });
    }

    formatDate(dateString: string): string {
        return new Date(dateString).toLocaleDateString("en-US", {
            year: "numeric",
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit",
        });
    }

    getShareUrl(shareToken: string): string {
        return `${window.location.origin}/recipe/share/${shareToken}`;
    }
} 