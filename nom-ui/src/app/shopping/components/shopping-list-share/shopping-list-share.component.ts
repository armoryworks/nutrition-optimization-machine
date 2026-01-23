import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil, finalize, forkJoin } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwCheckboxComponent,
  AmwProgressSpinnerComponent,
  AmwIconComponent,
} from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { NotificationService } from '../../../utilities/services/notification.service';

interface ShareTarget {
  id: number;
  name: string;
  email: string;
  isShared: boolean;
}

@Component({
  selector: 'nom-shopping-list-share',
  standalone: true,
  imports: [
    AmwCardComponent,
    AmwButtonComponent,
    AmwCheckboxComponent,
    AmwProgressSpinnerComponent,
    AmwIconComponent,
  ],
  templateUrl: './shopping-list-share.component.html',
  styleUrl: './shopping-list-share.component.scss',
})
export class ShoppingListShareComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private notificationService = inject(NotificationService);

  // Signals
  shoppingListId = signal<number>(0);
  shoppingList = signal<ShoppingListResponseModel | null>(null);
  shareTargets = signal<ShareTarget[]>([]);
  shareLink = signal<string>('');
  isLoading = signal(true);
  isSaving = signal(false);
  error = signal<string | null>(null);
  linkCopied = signal(false);

  // Computed
  hasTargets = computed(() => this.shareTargets().length > 0);
  sharedCount = computed(() => this.shareTargets().filter((t) => t.isShared).length);

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.shoppingListId.set(+id);
        this.loadData();
      } else {
        this.error.set('Invalid shopping list ID');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.shoppingService
      .getShoppingList(this.shoppingListId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (shoppingList) => {
          this.shoppingList.set(shoppingList);

          // Generate share link
          const origin = window.location.origin;
          this.shareLink.set(`${origin}/shopping/${shoppingList.id}`);

          // Mock share targets - in a real implementation, this would fetch household members
          // Since household integration isn't complete yet, we'll show a placeholder
          this.shareTargets.set([
            {
              id: 1,
              name: 'Household Members',
              email: 'members@household.com',
              isShared: true,
            },
          ]);
        },
        error: (err) => {
          this.error.set('Failed to load shopping list');
          console.error('Error loading shopping list:', err);
        },
      });
  }

  onToggleShare(targetId: number, isShared: boolean | null): void {
    if (isShared === null) return;
    const targets = this.shareTargets().map((t) =>
      t.id === targetId ? { ...t, isShared } : t
    );
    this.shareTargets.set(targets);
  }

  onSaveSharing(): void {
    this.isSaving.set(true);

    // Simulate save operation
    // In a real implementation, this would call a backend API to update sharing permissions
    setTimeout(() => {
      this.isSaving.set(false);
      this.notificationService.success('Sharing preferences saved');
    }, 1000);
  }

  onCopyLink(): void {
    const link = this.shareLink();
    if (!link) return;

    navigator.clipboard
      .writeText(link)
      .then(() => {
        this.linkCopied.set(true);
        this.notificationService.success('Link copied to clipboard');

        // Reset copied state after 3 seconds
        setTimeout(() => {
          this.linkCopied.set(false);
        }, 3000);
      })
      .catch((err) => {
        console.error('Failed to copy link:', err);
        this.notificationService.error('Failed to copy link');
      });
  }

  onBack(): void {
    this.router.navigate(['/shopping', this.shoppingListId()]);
  }

  onRetry(): void {
    this.loadData();
  }
}
