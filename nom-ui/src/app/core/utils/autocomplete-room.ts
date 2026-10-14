export const PHONE_QUERY = '(max-width: 767px)';
export const KEYBOARD_SETTLE_MS = 350;

/**
 * On phones, scrolls a focused autocomplete field to the top of the screen once
 * the keyboard has opened, so its suggestions open below it with room to show
 * several options instead of flipping above and clipping under the header.
 */
export function makeRoomForAutocomplete(target: EventTarget | null, view: Window): void {
  const field = target as HTMLElement | null;
  if (!field?.classList?.contains('mat-mdc-autocomplete-trigger')) return;
  if (!view.matchMedia?.(PHONE_QUERY).matches) return;
  view.setTimeout(() => {
    if (view.document.activeElement === field) {
      field.scrollIntoView({ block: 'start', behavior: 'smooth' });
    }
  }, KEYBOARD_SETTLE_MS);
}
