import { Component, OnInit } from '@angular/core';
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

import { RecipeService } from '../../services/recipe.service';
import { RecipeShareTokenModel, RecipeShareTokenCreateRequestModel, RecipeShareTokenResponseModel } from '../../models/recipe-share-token.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

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
export class RecipeShareTokenComponent implements OnInit {
  shareTokens: RecipeShareTokenResponseModel[] = [];
  isLoading = false;
  error: string | null = null;
  shareTokenForm: FormGroup;
  isAddingShareToken = false;

  constructor(
    private recipeService: RecipeService,
    private router: Router,
    private nonNullableFb: NonNullableFormBuilder,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {
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