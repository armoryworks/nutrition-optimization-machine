import { Component, DestroyRef, effect, inject, input, signal, untracked, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { DishGroupService } from '../core/services/dish-group.service';
import { LoadingService } from '../core/services/loading.service';
import { DishGroupDetailModel } from '../core/models/dish-group.model';

/** One dish group: every visible take on the dish, as a recipe-card grid. */
@Component({
  selector: 'nom-dish-group',
  imports: [DecimalPipe, RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule, ErrorBanner],
  templateUrl: './dish-group.component.html',
  styleUrl: './dish-group.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DishGroup {
  /** Route param (component input binding). */
  slug = input.required<string>();

  private dishGroupService = inject(DishGroupService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  group = signal<DishGroupDetailModel | null>(null);
  loading = signal(true);
  error = signal('');

  private loadOnSlug = effect(() => {
    const slug = this.slug();
    untracked(() => this.load(slug));
  });

  private load(slug: string): void {
    this.loading.set(true);
    this.error.set('');
    this.dishGroupService.getBySlug(slug).pipe(
      this.loadingService.loading('Loading dish...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (group) => {
        this.group.set(group);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Dish group not found.');
        this.loading.set(false);
      },
    });
  }
}
