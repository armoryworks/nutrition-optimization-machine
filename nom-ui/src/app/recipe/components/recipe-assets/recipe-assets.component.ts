import { Component, OnInit, inject } from '@angular/core';
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
import { RecipeAssetModel, RecipeAssetResponseModel } from '../../models/recipe-asset.model';


@Component({
    selector: 'nom-recipe-assets',
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
    templateUrl: './recipe-assets.component.html',
    styleUrls: ['./recipe-assets.component.scss']
})
export class RecipeAssetsComponent implements OnInit {
    private recipeService = inject(RecipeService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);

    assets: RecipeAssetResponseModel[] = [];
    isLoading = false;
    error: string | null = null;
    assetForm: FormGroup;
    isAddingAsset = false;



    constructor() {
        this.assetForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
            icon: ['', [Validators.required]],
            description: ['', [Validators.maxLength(500)]]
        });
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadAssets();
        }
    }

    loadAssets(): void {
        this.isLoading = true;
        this.error = null;

        this.recipeAssetsService.getRecipeAssets(this.recipeId).subscribe({
            next: (assets) => {
                this.assets = assets;
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading recipe assets:', error);
                this.error = 'Failed to load recipe assets';
                this.isLoading = false;
                this.snackBar.open('Failed to load recipe assets', 'Close', { duration: 3000 });
            }
        });
    }

    onRefresh(): void {
        this.loadAssets();
    }

    onRetry(): void {
        this.loadAssets();
    }

    onFileSelected(event: Event): void {
        const file = (event.target as HTMLInputElement).files?.[0];
        if (file) {
            this.selectedFile = file;
            // Auto-set name if not already set
            if (!this.assetForm.get('name')?.value) {
                this.assetForm.patchValue({ name: file.name });
            }
        }
    }

    onSubmit(): void {
        if (this.assetForm.invalid || !this.selectedFile) {
            return;
        }

        this.isSubmitting = true;
        const formData = this.assetForm.value;

        const assetData = {
            name: formData.name,
            icon: formData.icon,
            description: formData.description,
            fileName: this.selectedFile.name,
            fileSize: this.selectedFile.size,
            mimeType: this.selectedFile.type
        };

        this.recipeAssetsService.createRecipeAsset(this.recipeId, assetData, this.selectedFile).subscribe({
            next: (newAsset) => {
                this.assets.push(newAsset);
                this.assetsChanged.emit(this.assets);
                this.resetForm();
                this.snackBar.open('Asset uploaded successfully', 'Close', { duration: 3000 });
                this.isSubmitting = false;
            },
            error: (error) => {
                console.error('Error uploading asset:', error);
                this.snackBar.open('Failed to upload asset', 'Close', { duration: 3000 });
                this.isSubmitting = false;
            }
        });
    }

    onDeleteAsset(assetId: number): void {
        const dialogRef = this.dialog.open(ConfirmDeleteDialogComponent, {
            width: '400px',
            data: { message: 'Are you sure you want to delete this asset?' }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.recipeAssetsService.deleteRecipeAsset(assetId).subscribe({
                    next: () => {
                        this.assets = this.assets.filter(asset => asset.id !== assetId);
                        this.assetsChanged.emit(this.assets);
                        this.snackBar.open('Asset deleted successfully', 'Close', { duration: 3000 });
                    },
                    error: (error) => {
                        console.error('Error deleting asset:', error);
                        this.snackBar.open('Failed to delete asset', 'Close', { duration: 3000 });
                    }
                });
            }
        });
    }

    onDownloadAsset(asset: RecipeAssetModel): void {
        this.recipeAssetsService.downloadAsset(asset.id).subscribe({
            next: (blob) => {
                const url = window.URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = asset.fileName || asset.name;
                link.click();
                window.URL.revokeObjectURL(url);
            },
            error: (error) => {
                console.error('Error downloading asset:', error);
                this.snackBar.open('Failed to download asset', 'Close', { duration: 3000 });
            }
        });
    }

    getIconClass(icon: string): string {
        const iconOption = this.iconOptions.find(option => option.value === icon);
        return iconOption?.icon || 'description';
    }

    getFileSizeDisplay(bytes: number): string {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    formatDate(date: string): string {
        return new Date(date).toLocaleDateString();
    }

    private resetForm(): void {
        this.assetForm.reset({ icon: 'file' });
        this.selectedFile = null;
        const fileInput = document.getElementById('fileInput') as HTMLInputElement;
        if (fileInput) {
            fileInput.value = '';
        }
    }
}

@Component({
    selector: 'nom-confirm-delete-dialog',
    template: `
        <h2 mat-dialog-title>Confirm Delete</h2>
        <mat-dialog-content>{{ data.message }}</mat-dialog-content>
        <mat-dialog-actions align="end">
            <button mat-button mat-dialog-close>Cancel</button>
            <button mat-raised-button color="warn" [mat-dialog-close]="true">Delete</button>
        </mat-dialog-actions>
    `,
    standalone: true,
    imports: [CommonModule, MatDialogModule, MatButtonModule]
})
export class ConfirmDeleteDialogComponent {
    data = inject({ message: string });


} 