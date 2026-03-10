import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { CookbookService } from '../core/services/cookbook.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { CookbookResponseModel } from '../core/models/cookbook-response.model';
import { CookbookFormDialog, CookbookFormDialogData, CookbookFormDialogResult } from './cookbook-form-dialog.component';
import { ConfirmDeleteDialog, ConfirmDeleteDialogData } from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-cookbook-list',
  standalone: true,
  imports: [
    RouterLink,

    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './cookbook-list.component.html',
  styleUrls: ['./cookbook-list.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CookbookList implements OnInit {
  private cookbookService = inject(CookbookService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);

  cookbooks = signal<CookbookResponseModel[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  householdId = signal(0);

  ngOnInit(): void {
    this.loadHouseholdThenCookbooks();
  }

  private loadHouseholdThenCookbooks(): void {
    this.loading.set(true);
    this.error.set(null);

    this.householdService.getHouseholds().subscribe({
      next: (households) => {
        if (households.length > 0) {
          this.householdId.set(households[0].id);
          this.loadCookbooks();
        } else {
          this.error.set('No household found. Create a household first.');
          this.loading.set(false);
        }
      },
      error: () => {
        this.error.set('Failed to load household.');
        this.loading.set(false);
      },
    });
  }

  private loadCookbooks(): void {
    this.loading.set(true);
    this.error.set(null);

    this.cookbookService.getCookbooks(this.householdId()).subscribe({
      next: (cookbooks) => {
        this.cookbooks.set(cookbooks);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load cookbooks.');
        this.loading.set(false);
      },
    });
  }

  onCreate(): void {
    const dialogRef = this.dialog.open(CookbookFormDialog, {
      data: {} as CookbookFormDialogData,
    });

    dialogRef.afterClosed().subscribe((result: CookbookFormDialogResult | undefined) => {
      if (result) {
        this.cookbookService.createCookbook({
          householdId: this.householdId(),
          name: result.name,
          description: result.description || undefined,
          isPublic: result.isPublic,
        }).subscribe({
          next: () => this.loadCookbooks(),
          error: () => this.error.set('Failed to create cookbook.'),
        });
      }
    });
  }

  onEdit(cookbook: CookbookResponseModel): void {
    const dialogRef = this.dialog.open(CookbookFormDialog, {
      data: { cookbook } as CookbookFormDialogData,
    });

    dialogRef.afterClosed().subscribe((result: CookbookFormDialogResult | undefined) => {
      if (result) {
        this.cookbookService.updateCookbook(cookbook.id, {
          name: result.name,
          description: result.description || undefined,
          isPublic: result.isPublic,
        }).subscribe({
          next: () => this.loadCookbooks(),
          error: () => this.error.set('Failed to update cookbook.'),
        });
      }
    });
  }

  onDelete(cookbook: CookbookResponseModel): void {
    const dialogRef = this.dialog.open(ConfirmDeleteDialog, {
      data: {
        title: 'Delete Cookbook',
        message: `Are you sure you want to delete "${cookbook.name}"? This cannot be undone.`,
        confirmText: 'Delete',
      } as ConfirmDeleteDialogData,
    });

    dialogRef.afterClosed().subscribe((confirmed: boolean) => {
      if (confirmed) {
        this.cookbookService.deleteCookbook(cookbook.id).subscribe({
          next: () => this.loadCookbooks(),
          error: () => this.error.set('Failed to delete cookbook.'),
        });
      }
    });
  }
}
