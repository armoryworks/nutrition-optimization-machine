import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { AdminService } from '../core/services/admin.service';
import { LoadingService } from '../core/services/loading.service';
import { AdminUser, UserClaims } from '../core/models/user-management.model';
import {
  ConfirmDeleteDialog,
  ConfirmDeleteDialogData,
} from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-users-admin',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatDialogModule,
    ErrorBanner,
  ],
  templateUrl: './users-admin.component.html',
  styleUrl: './users-admin.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersAdmin implements OnInit {
  private adminService = inject(AdminService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  users = signal<AdminUser[]>([]);
  loading = signal(true);
  errorMessage = signal('');
  expandedId = signal<string | null>(null);
  claims = signal<UserClaims | null>(null);
  claimsLoading = signal(false);
  saving = signal(false);

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.adminService
      .getUsers()
      .pipe(this.loadingService.loading('Loading users...'), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (users) => {
          this.users.set(users);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.errorMessage.set('Failed to load users. You may not have permission.');
        },
      });
  }

  toggleExpand(user: AdminUser): void {
    if (this.expandedId() === user.id) {
      this.expandedId.set(null);
      return;
    }
    this.expandedId.set(user.id);
    this.claims.set(null);
    this.claimsLoading.set(true);
    this.adminService
      .getUserClaims(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (claims) => {
          this.claims.set(claims);
          this.claimsLoading.set(false);
        },
        error: () => {
          this.claimsLoading.set(false);
          this.errorMessage.set('Failed to load claims for this user.');
        },
      });
  }

  setClaim(claim: 'canManageCuration' | 'canManageUserRoles', value: boolean): void {
    const current = this.claims();
    if (!current || this.saving()) return;
    const updated = { ...current, [claim]: value };
    this.saving.set(true);
    this.adminService
      .updateUserClaims({
        userId: current.userId,
        canManageCuration: updated.canManageCuration,
        canManageUserRoles: updated.canManageUserRoles,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.claims.set(updated);
          this.saving.set(false);
          this.snackBar.open('Claims updated', undefined, { duration: 2500 });
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Failed to update claims', undefined, { duration: 4000 });
        },
      });
  }

  deleteUser(user: AdminUser): void {
    this.dialog
      .open(ConfirmDeleteDialog, {
        data: {
          title: 'Delete User',
          message: `Delete user "${user.username}" (${user.email})? This cannot be undone.`,
          confirmText: 'Delete',
        } satisfies ConfirmDeleteDialogData,
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.adminService
          .deleteUser(user.id)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: () => {
              this.users.update((list) => list.filter((u) => u.id !== user.id));
              if (this.expandedId() === user.id) this.expandedId.set(null);
              this.snackBar.open('User deleted', undefined, { duration: 2500 });
            },
            error: () => this.snackBar.open('Failed to delete user', undefined, { duration: 4000 }),
          });
      });
  }
}
