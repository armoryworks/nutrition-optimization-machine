import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RecipeModel } from '../../../core/models/recipe.model';
import { EntityPreviewService } from './entity-preview.service';
import { EntityPreviewPopover } from './entity-preview-popover.component';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { EntityPreviewSheet, EntityPreviewSheetData } from './entity-preview-sheet.component';

/**
 * Inline link to another record (recipe, ingredient search, member…). Renders
 * as part of the surrounding text and stops click propagation, so it is safe
 * inside clickable rows/cells (plan cells, list rows) without triggering the
 * parent's handler.
 *
 * Without `previewRecipeId`, it navigates like a normal link. With it, a click
 * does NOT navigate — it opens a closable details popover whose "Open recipe"
 * link performs the actual navigation (Esc / outside click / ✕ close it).
 * Modified clicks (ctrl/cmd/middle) bypass the popover and open a new tab.
 * Details are fetched through the normal authorized recipe endpoint, so
 * server-side visibility rules are inherited.
 */
@Component({
  selector: 'nom-entity-link',
  imports: [RouterLink],
  templateUrl: './entity-link.component.html',
  styleUrl: './entity-link.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityLink {
  /** Router target, e.g. `['/recipe', id]` or `'/search'`. */
  route = input.required<string | readonly unknown[]>();
  /** Visible link text. */
  label = input.required<string>();
  queryParams = input<Params | null>(null);
  /** data-testid for the anchor; hosts pass their page-scoped id. */
  testid = input('entity-link');
  /** Accessible label when the visible text alone is ambiguous. */
  ariaLabel = input('');
  /** When set, clicking opens the details popover instead of navigating. */
  previewRecipeId = input<number | null>(null);

  private overlay = inject(Overlay);
  private bottomSheet = inject(MatBottomSheet);
  private previewService = inject(EntityPreviewService);
  private elementRef = inject(ElementRef);
  private destroyRef = inject(DestroyRef);

  private overlayRef: OverlayRef | null = null;
  private recipe = signal<RecipeModel | null>(null);
  private loadFailed = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => this.closePopover());
  }

  onActivate(event: Event): void {
    event.stopPropagation();
    const id = this.previewRecipeId();
    if (!id) return;

    // Modified clicks keep native open-in-new-tab behavior.
    const mouse = event as MouseEvent;
    if (mouse.ctrlKey || mouse.metaKey || mouse.shiftKey || mouse.button === 1) return;

    event.preventDefault();
    // Matches the SCSS $breakpoint-mobile token (768px): phones get the
    // curated bottom sheet, larger screens the anchored popover.
    if (window.matchMedia('(max-width: 768px)').matches) {
      this.openSheet(id);
      return;
    }
    if (this.overlayRef) {
      this.closePopover();
    } else {
      this.openPopover(id);
    }
  }

  private openSheet(id: number): void {
    const data: EntityPreviewSheetData = {
      recipeId: id,
      route: this.route(),
      queryParams: this.queryParams(),
    };
    this.bottomSheet.open(EntityPreviewSheet, { data });
  }

  private openPopover(id: number): void {
    const positionStrategy = this.overlay
      .position()
      .flexibleConnectedTo(this.elementRef)
      .withPositions([
        { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 6 },
        { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -6 },
      ]);

    this.overlayRef = this.overlay.create({
      positionStrategy,
      scrollStrategy: this.overlay.scrollStrategies.reposition(),
      hasBackdrop: false,
    });

    this.overlayRef
      .outsidePointerEvents()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.closePopover());
    this.overlayRef
      .keydownEvents()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((e) => {
        if (e.key === 'Escape') this.closePopover();
      });

    const componentRef = this.overlayRef.attach(new ComponentPortal(EntityPreviewPopover));
    componentRef.setInput('recipe', this.recipe());
    componentRef.setInput('loadFailed', this.loadFailed());
    componentRef.setInput('route', this.route());
    componentRef.setInput('queryParams', this.queryParams());
    componentRef.instance.closed.subscribe(() => this.closePopover());

    this.previewService
      .recipe(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (recipe) => {
          this.recipe.set(recipe);
          componentRef.setInput('recipe', recipe);
        },
        error: () => {
          this.loadFailed.set(true);
          componentRef.setInput('loadFailed', true);
        },
      });
  }

  private closePopover(): void {
    this.overlayRef?.dispose();
    this.overlayRef = null;
  }
}
