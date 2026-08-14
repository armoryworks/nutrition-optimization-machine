import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { DishGroupService } from '../core/services/dish-group.service';
import { LoadingService } from '../core/services/loading.service';
import { DishGroupModel } from '../core/models/dish-group.model';

/** Browse the canonical dish groups ("chocolate chip cookies"), largest first. */
@Component({
  selector: 'nom-dish-groups',
  imports: [RouterLink, MatIconModule, MatProgressSpinnerModule, ErrorBanner],
  templateUrl: './dish-groups.component.html',
  styleUrl: './dish-groups.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DishGroups implements OnInit {
  private dishGroupService = inject(DishGroupService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  groups = signal<DishGroupModel[]>([]);
  loading = signal(true);
  error = signal('');

  ngOnInit(): void {
    this.dishGroupService.list().pipe(
      this.loadingService.loading('Loading dishes...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (groups) => {
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
