import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Params } from '@angular/router';
import { RouterLink } from '@angular/router';

/**
 * Inline navigable link to another record (recipe, ingredient search, member…).
 * Renders as part of the surrounding text and stops click propagation, so it is
 * safe inside clickable rows/cells (plan cells, list rows) without triggering
 * the parent's handler.
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

  onActivate(event: Event): void {
    event.stopPropagation();
  }
}
