import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { DishGroupService } from '../core/services/dish-group.service';
import { AuthService } from '../core/services/auth.service';
import { LoadingService } from '../core/services/loading.service';
import { DishGroupModel } from '../core/models/dish-group.model';
import {
  MergeDishGroupDialog,
  MergeDishGroupDialogData,
  MergeDishGroupDialogResult,
} from './merge-dish-group-dialog/merge-dish-group-dialog.component';

/** Browse the canonical dish groups ("chocolate chip cookies"), largest first. */
@Component({
  selector: 'nom-dish-groups',
  imports: [RouterLink, MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, ErrorBanner],
  templateUrl: './dish-groups.component.html',
  styleUrl: './dish-groups.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DishGroups implements OnInit {
  private dishGroupService = inject(DishGroupService);
  private authService = inject(AuthService);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

  groups = signal<DishGroupModel[]>([]);
  /** Zero-count groups are hidden from browsing but stay valid merge targets. */
  private allGroups = signal<DishGroupModel[]>([]);
  loading = signal(true);
  error = signal('');

  /** Merge is a curation action; the UI's proxy for CanManageCuration is admin status. */
  isAdmin = this.authService.isAdmin;

  ngOnInit(): void {
    this.loadGroups();
    if (this.authService.isLoggedIn()) {
      this.authService.ensureAdminStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    }
  }

  /** Curation admins: merge this group's recipes into another and retire it. */
  openMergeDialog(event: Event, source: DishGroupModel): void {
    // The affordance sits inside the card link — don't navigate.
    event.preventDefault();
    event.stopPropagation();

    const dialogRef = this.dialog.open(MergeDishGroupDialog, {
      width: '480px',
      data: { source, groups: this.allGroups() } as MergeDishGroupDialogData,
    });

    dialogRef.afterClosed().subscribe((merged: MergeDishGroupDialogResult) => {
      if (merged) {
        this.loadGroups();
      }
    });
  }

  private loadGroups(): void {
    this.loading.set(true);
    this.error.set('');
    this.dishGroupService.list().pipe(
      this.loadingService.loading('Loading dishes...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (groups) => {
        this.allGroups.set(groups);
        this.groups.set(groups.filter(g => g.recipeCount > 0));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load dish groups.');
        this.loading.set(false);
      },
    });
  }
}
