import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RecipeImageCredit } from '../../../core/models/recipe-image.model';

/**
 * Credit line for a third-party openly licensed photo: "Photo by {author} on {source} · {licence}",
 * each part linked when the source provided a link. Licences such as CC BY and the Unsplash
 * API terms require this wherever the photo is shown.
 */
@Component({
  selector: 'nom-image-credit',
  templateUrl: './image-credit.component.html',
  styleUrl: './image-credit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImageCredit {
  readonly credit = input.required<RecipeImageCredit>();
  readonly testId = input('recipe-image-credit');
}
