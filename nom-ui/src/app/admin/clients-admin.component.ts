import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { AdminService } from '../core/services/admin.service';
import { LoadingService } from '../core/services/loading.service';
import { AdminHousehold, AdminHouseholdMember } from '../core/models/client-admin.model';

/**
 * Admin portal: the client roster. Households are the client unit — a family,
 * a coach's client, a dietitian's patient — so this view is household-first,
 * with the accounts inside each one a row-expand away (deeper account actions
 * live in the Users tab).
 */
@Component({
  selector: 'nom-clients-admin',
  imports: [DatePipe, MatIconModule, MatProgressSpinnerModule, ErrorBanner],
  templateUrl: './clients-admin.component.html',
  styleUrl: './clients-admin.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientsAdmin implements OnInit {
  private adminService = inject(AdminService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  households = signal<AdminHousehold[]>([]);
  loading = signal(true);
  errorMessage = signal('');
  filter = signal('');
  expandedId = signal<number | null>(null);
  members = signal<AdminHouseholdMember[]>([]);
  membersLoading = signal(false);

  filtered = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (!q) return this.households();
    return this.households().filter((h) => h.name.toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.loadHouseholds();
  }

  loadHouseholds(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.adminService
      .getHouseholds()
      .pipe(this.loadingService.loading('Loading clients...'), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (households) => {
          this.households.set(households);
          this.loading.set(false);
        },
        error: () => {
          this.errorMessage.set('Failed to load clients.');
          this.loading.set(false);
        },
      });
  }

  onFilterInput(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  toggleExpand(household: AdminHousehold): void {
    if (this.expandedId() === household.id) {
      this.expandedId.set(null);
      return;
    }
    this.expandedId.set(household.id);
    this.members.set([]);
    this.membersLoading.set(true);
    this.adminService
      .getHouseholdMembers(household.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (members) => {
          this.members.set(members);
          this.membersLoading.set(false);
        },
        error: () => {
          this.errorMessage.set('Failed to load household members.');
          this.membersLoading.set(false);
        },
      });
  }

  /** "Managed" | "Personal" | "Shared" — the household's client type. */
  kind(h: AdminHousehold): string {
    if (h.managedBy) return 'Managed';
    return h.isPersonal ? 'Personal' : 'Shared';
  }
}
