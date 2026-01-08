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
import { MatSelectModule } from '@angular/material/select';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { Subject, takeUntil } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeShareTokenResponseModel } from '../../models/recipe-share-token.model';

@Component({
    selector: 'nom-recipe-share-token',
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
        MatSelectModule,
        MatDialogModule,
        MatListModule,
        MatMenuModule,
    ],
    templateUrl: './recipe-share-token.component.html',
    styleUrls: ['./recipe-share-token.component.scss']
})
export class RecipeShareTokenComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);
    private destroy$ = new Subject<void>();

    recipeId = input<number>();

    shareTokens = signal<RecipeShareTokenResponseModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    shareTokenForm: FormGroup;
    isAddingShareToken = signal(false);
    isSubmitting = signal(false);



    constructor() {
        this.shareTokenForm = this.nonNullableFb.group({
            shareName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
            isPublic: [false, [Validators.required]]
        });
    }

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
        this.isLoading.set(true);
        this.error.set(null);
        this.recipeService
            .getRecipeShareTokens(this.recipeId()!)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (shareTokens) => {
                    this.shareTokens.set(shareTokens);
                    this.isLoading.set(false);
                },
                error: (error) => {
                    console.error("Error loading share tokens:", error);
                    this.error.set("Failed to load share tokens. Please try again.");
                    this.isLoading.set(false);
                },
            });
    }

    createShareToken(): void {
        if (this.shareTokenForm.invalid || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        const request: any = {
            recipeId: this.recipeId(),
            shareName: this.shareTokenForm.get("shareName")!.value || undefined,
            isPublic: this.shareTokenForm.get("isPublic")!.value,
        };

        this.recipeService
            .createShareToken(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (shareToken) => {
                    this.shareTokens.set([shareToken, ...this.shareTokens()]);
                    this.shareTokenForm.reset();
                    this.snackBar.open("Share token created successfully", "Close", { duration: 3000 });
                    this.isSubmitting.set(false);
                },
                error: (error) => {
                    console.error("Error creating share token:", error);
                    this.error.set("Failed to create share token. Please try again.");
                    this.isSubmitting.set(false);
                },
            });
    }

    deleteShareToken(shareTokenId: number): void {
        this.recipeService
            .deleteShareToken(shareTokenId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.shareTokens.set(this.shareTokens().filter(t => t.id !== shareTokenId));
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
    trackByShareTokenId(index: number, shareToken: RecipeShareTokenResponseModel): number {
        return shareToken.id;
    }
}
