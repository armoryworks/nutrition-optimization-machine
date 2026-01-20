import { Component, OnInit, OnDestroy, inject, signal, input } from '@angular/core';
import { NgFor, NgIf } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NotificationService } from '../../../utilities/services/notification.service';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwInputComponent, AmwSelectComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwChipComponent, AmwDividerComponent } from 'angular-material-wrap';

import { RecipeService } from '../../services/recipe.service';
import { RecipeShareTokenResponseModel } from '../../models/recipe-share-token.model';

@Component({
    selector: 'nom-recipe-share-token',
    standalone: true,
    imports: [
        NgFor,
        NgIf,
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwSelectComponent,
        AmwIconButtonComponent,
        AmwTooltipDirective,
        AmwIconComponent,
        AmwChipComponent,
        AmwDividerComponent,
    ],
    templateUrl: './recipe-share-token.component.html',
    styleUrls: ['./recipe-share-token.component.scss']
})
export class RecipeShareTokenComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
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
                    this.notificationService.success("Share token created successfully");
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
                    this.notificationService.success("Share token deleted successfully");
                },
                error: (error) => {
                    console.error("Error deleting share token:", error);
                    this.notificationService.error("Failed to delete share token");
                },
            });
    }

    copyShareToken(shareToken: string): void {
        navigator.clipboard.writeText(this.getShareUrl(shareToken)).then(() => {
            this.notificationService.success("Share URL copied to clipboard");
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
