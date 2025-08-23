import { Component, OnInit, inject, OnDestroy, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatSnackBarModule, MatSnackBar } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subject, takeUntil } from 'rxjs';
import { RecipeAssetsService } from '../../services/recipe-assets.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { ConfirmDeleteDialogComponent } from '../confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
    selector: 'app-recipe-assets',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatSelectModule,
        MatIconModule,
        MatDialogModule,
        MatSnackBarModule,
        MatProgressBarModule
    ],
    templateUrl: './recipe-assets.component.html',
    styleUrls: ['./recipe-assets.component.scss']
})
export class RecipeAssetsComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private recipeAssetsService = inject(RecipeAssetsService);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);
    private configurationService = inject(ConfigurationService);

    // Input properties
    @Input() recipeId: number = 0;
    @Input() isEditMode: boolean = false;

    // Component state
    assets: any[] = [];
    assetForm: FormGroup;
    selectedFile: File | null = null;
    isSubmitting = false;
    uploadProgress = 0;
    isLoading = false;
    error: string | null = null;

    // Icon options for assets
    iconOptions = [
        { value: 'image', icon: 'image', label: 'Image' },
        { value: 'video', icon: 'video_library', label: 'Video' },
        { value: 'document', icon: 'description', label: 'Document' },
        { value: 'audio', icon: 'audiotrack', label: 'Audio' },
        { value: 'file', icon: 'insert_drive_file', label: 'File' }
    ];

    private destroy$ = new Subject<void>();

    constructor() {
        this.assetForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(100)]],
            description: ['', [Validators.maxLength(500)]],
            icon: ['file', Validators.required]
        });
    }

    ngOnInit(): void {
        this.loadAssets();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    private loadAssets(): void {
        if (!this.recipeId) {
            this.error = 'Recipe ID is required';
            return;
        }

        this.isLoading = true;
        this.error = null;

        this.recipeAssetsService.getRecipeAssets(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (assets: any) => {
                    this.assets = assets;
                    this.isLoading = false;
                },
                error: (error: any) => {
                    console.error('Error loading assets:', error);
                    this.error = 'Failed to load assets. Please try again.';
                    this.isLoading = false;
                    this.snackBar.open('Failed to load assets', 'Close', { duration: 3000 });
                }
            });
    }

    onFileSelected(event: any): void {
        const file = event.target.files[0];
        if (file) {
            // Validate file type and size
            if (!this.configurationService.isFileTypeAllowed(file.name, 'image')) {
                this.snackBar.open('Invalid file type. Please select an image file.', 'Close', { duration: 3000 });
                return;
            }

            if (!this.configurationService.isFileSizeAllowed(file.size)) {
                this.snackBar.open('File size too large. Maximum size is 10MB.', 'Close', { duration: 3000 });
                return;
            }

            this.selectedFile = file;
            this.assetForm.patchValue({ name: file.name });
        }
    }

    onSubmit(): void {
        if (this.assetForm.valid && this.selectedFile) {
            this.isSubmitting = true;
            this.uploadProgress = 0;

            // Use the existing createRecipeAsset method
            const assetData = {
                name: this.assetForm.get('name')?.value,
                icon: this.assetForm.get('icon')?.value,
                description: this.assetForm.get('description')?.value,
                fileName: this.selectedFile?.name || '',
                fileSize: this.selectedFile?.size || 0,
                mimeType: this.selectedFile?.type || ''
            };

            this.recipeAssetsService.createRecipeAsset(this.recipeId, assetData, this.selectedFile!)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: (newAsset: any) => {
                        this.assets.push(newAsset);
                        this.resetForm();
                        this.snackBar.open('Asset uploaded successfully', 'Close', { duration: 3000 });
                        this.isSubmitting = false;
                    },
                    error: (error: any) => {
                        console.error('Error uploading asset:', error);
                        this.snackBar.open('Failed to upload asset', 'Close', { duration: 3000 });
                        this.isSubmitting = false;
                    }
                });
        }
    }

    onDeleteAsset(assetId: number): void {
        const dialogRef = this.dialog.open(ConfirmDeleteDialogComponent, {
            width: '400px',
            data: { message: 'Are you sure you want to delete this asset?' }
        });

        dialogRef.afterClosed().subscribe((result: any) => {
            if (result) {
                this.recipeAssetsService.deleteRecipeAsset(assetId).subscribe({
                    next: () => {
                        this.assets = this.assets.filter(asset => asset.id !== assetId);
                        this.snackBar.open('Asset deleted successfully', 'Close', { duration: 3000 });
                    },
                    error: (error: any) => {
                        console.error('Error deleting asset:', error);
                        this.snackBar.open('Failed to delete asset', 'Close', { duration: 3000 });
                    }
                });
            }
        });
    }

    onDownloadAsset(asset: any): void {
        this.recipeAssetsService.downloadAsset(asset.id).subscribe({
            next: (blob: any) => {
                const url = window.URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = asset.fileName || asset.name;
                link.click();
                window.URL.revokeObjectURL(url);
            },
            error: (error: any) => {
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
        const sizes = this.configurationService.getFileSizeUnits();
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

    // Template methods
    onRefresh(): void {
        this.loadAssets();
    }

    onRetry(): void {
        this.error = null;
        this.loadAssets();
    }

    get listConfig(): any {
        return {
            title: 'Recipe Assets',
            subtitle: 'Manage files, images, and documents for this recipe',
            showBackButton: true,
            showRefreshButton: true
        };
    }
} 