import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { RecipeModel } from '../../../core/models/recipe.model';

/**
 * Closable details card opened by nom-entity-link. Shows the record's details
 * and carries the ACTUAL navigation link ("Open recipe"); positioning is the
 * opener's job.
 */
@Component({
  selector: 'nom-entity-preview-popover',
  imports: [RouterLink, MatIconModule],
  templateUrl: './entity-preview-popover.component.html',
  styleUrl: './entity-preview-popover.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityPreviewPopover {
  recipe = input<RecipeModel | null>(null);
  loadFailed = input(false);
  /** Target of the "Open recipe" link inside the card. */
  route = input<string | readonly unknown[] | null>(null);
  queryParams = input<Params | null>(null);

  closed = output<void>();

  totalMinutes(): number | null {
    const r = this.recipe();
    if (!r) return null;
    const total = (r.prepTimeMinutes ?? 0) + (r.cookTimeMinutes ?? 0);
    return total > 0 ? total : null;
  }
}
