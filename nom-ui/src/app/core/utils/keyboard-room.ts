import { KEYBOARD_SETTLE_MS, PHONE_QUERY } from './autocomplete-room';

/**
 * On phones, re-centres a focused field once the keyboard has opened, so a
 * field near the bottom of a sheet is not left hidden under the keyboard.
 */
export function keepFieldVisible(target: EventTarget | null, view: Window): void {
  const field = target as HTMLElement | null;
  if (!field?.scrollIntoView) return;
  if (!view.matchMedia?.(PHONE_QUERY).matches) return;
  view.setTimeout(() => {
    if (view.document.activeElement === field) {
      field.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }, KEYBOARD_SETTLE_MS);
}
