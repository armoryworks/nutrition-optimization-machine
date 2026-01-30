import { Component, signal, output } from '@angular/core';
import { AmwButtonComponent } from 'angular-material-wrap';

@Component({
  selector: 'nom-recipe-description-dialog',
  standalone: true,
  imports: [AmwButtonComponent],
  template: `
    <div class="recipe-description-dialog">
      <h2 class="recipe-description-dialog__title">{{ recipeName() }}</h2>
      <p class="recipe-description-dialog__text">{{ description() }}</p>
      <div class="recipe-description-dialog__actions">
        <amw-button variant="filled" color="primary" (click)="close.emit()">Close</amw-button>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }
    .recipe-description-dialog {
      padding: var(--amw-spacing-4, 1rem);
      background: var(--mat-sys-surface-container);
      border-radius: var(--nom-border-radius, 8px);
      color: var(--mat-sys-on-surface);
    }
    .recipe-description-dialog__title {
      margin: 0 0 var(--amw-spacing-3, 0.75rem);
      font-size: 1.25rem;
      font-weight: 600;
      color: var(--mat-sys-on-surface);
    }
    .recipe-description-dialog__text {
      margin: 0 0 var(--amw-spacing-4, 1rem);
      line-height: 1.6;
      color: var(--mat-sys-on-surface-variant);
    }
    .recipe-description-dialog__actions {
      display: flex;
      justify-content: flex-end;
    }
  `]
})
export class RecipeDescriptionDialogComponent {
  recipeName = signal('');
  description = signal('');
  close = output<void>();
}
