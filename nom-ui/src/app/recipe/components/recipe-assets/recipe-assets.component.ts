import { Component, OnInit, inject, OnDestroy, signal, input, computed } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { NotificationService } from '../../../utilities/services/notification.service';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwCardComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwProgressSpinnerComponent, AmwListComponent, AmwListItemComponent, DialogService } from 'angular-material-wrap';

import { RecipeAssetsService } from '../../services/recipe-assets.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { BasePageComponent } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'app-recipe-assets',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwSelectComponent,
        AmwTextareaComponent,
        AmwCardComponent,
        AmwIconButtonComponent,
        AmwTooltipDirective,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
        AmwListComponent,
        AmwListItemComponent,
        BasePageComponent,
    ],
    templateUrl: './recipe-assets.component.html',
    styleUrls: ['./recipe-assets.component.scss']
})
export class RecipeAssetsComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private recipeAssetsService = inject(RecipeAssetsService);
    private notificationService = inject(NotificationService);
    private dialogService = inject(DialogService);
    private configurationService = inject(ConfigurationService);

    // Input properties
    recipeId = input<number>(0);
    isEditMode = input<boolean>(false);

    // Component state
    assets = signal<any[]>([]);
    assetForm: FormGroup;
    selectedFile: File | null = null;
    isSubmitting = signal(false);
    uploadProgress = signal(0);
    isLoading = signal(false);
    error = signal<string | null>(null);

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
        if (!this.recipeId()) {
            this.error.set('Recipe ID is required');
            return;
        }

        this.isLoading.set(true);
        this.error.set(null);

        this.recipeAssetsService.getRecipeAssets(this.recipeId())
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (assets: any) => {
                    this.assets.set(assets);
                    this.isLoading.set(false);
                },
                error: (error: any) => {
                    console.error('Error loading assets:', error);
                    this.error.set('Failed to load assets. Please try again.');
                    this.isLoading.set(false);
                    this.notificationService.error('Failed to load assets');
                }
            });
    }

    onFileSelected(event: any): void {
        const file = event.target.files[0];
        if (file) {
            // Validate file type and size
            if (!this.configurationService.isFileTypeAllowed(file.name, 'image')) {
                this.notificationService.error('Invalid file type. Please select an image file.');
                return;
            }

            if (!this.configurationService.isFileSizeAllowed(file.size)) {
                this.notificationService.error('File size too large. Maximum size is 10MB.');
                return;
            }

            this.selectedFile = file;
            this.assetForm.patchValue({ name: file.name });
        }
    }

    onSubmit(): void {
        if (this.assetForm.valid && this.selectedFile) {
            this.isSubmitting.set(true);
            this.uploadProgress.set(0);

            // Use the existing createRecipeAsset method
            const assetData = {
                name: this.assetForm.get('name')?.value,
                icon: this.assetForm.get('icon')?.value,
                description: this.assetForm.get('description')?.value,
                fileName: this.selectedFile?.name || '',
                fileSize: this.selectedFile?.size || 0,
                mimeType: this.selectedFile?.type || ''
            };

            this.recipeAssetsService.createRecipeAsset(this.recipeId(), assetData, this.selectedFile!)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: (newAsset: any) => {
                        this.assets.set([...this.assets(), newAsset]);
                        this.resetForm();
                        this.notificationService.success('Asset uploaded successfully');
                        this.isSubmitting.set(false);
                    },
                    error: (error: any) => {
                        console.error('Error uploading asset:', error);
                        this.notificationService.error('Failed to upload asset');
                        this.isSubmitting.set(false);
                    }
                });
        }
    }

    onDeleteAsset(assetId: number): void {
        this.dialogService.confirm('Are you sure you want to delete this asset?', 'Confirm Delete')
            .pipe(takeUntil(this.destroy$))
            .subscribe((confirmed) => {
                if (confirmed) {
                    this.recipeAssetsService.deleteRecipeAsset(assetId).subscribe({
                        next: () => {
                            this.assets.set(this.assets().filter(asset => asset.id !== assetId));
                            this.notificationService.success('Asset deleted successfully');
                        },
                        error: (error: any) => {
                            console.error('Error deleting asset:', error);
                            this.notificationService.error('Failed to delete asset');
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
                this.notificationService.error('Failed to download asset');
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
        this.error.set(null);
        this.loadAssets();
    }

    listConfig = computed(() => ({
        title: 'Recipe Assets',
        subtitle: 'Manage files, images, and documents for this recipe',
        showBackButton: true,
        showRefreshButton: true
    }));
} 