import { CanDeactivateFn } from '@angular/router';

export interface HasUnsavedForm {
  hasUnsavedChanges(): boolean;
}

export const unsavedFormGuard: CanDeactivateFn<HasUnsavedForm> = (component) => {
  if (!component.hasUnsavedChanges()) return true;
  return confirm('Discard what you have entered on this page?');
};
