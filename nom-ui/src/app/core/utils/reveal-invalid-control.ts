import { AbstractControl } from '@angular/forms';

const INVALID_CONTROL_SELECTOR = [
  'input.ng-invalid',
  'textarea.ng-invalid',
  'select.ng-invalid',
  'mat-select.ng-invalid',
  'mat-button-toggle-group.ng-invalid',
  'mat-checkbox.ng-invalid',
  'mat-slide-toggle.ng-invalid',
  '[formcontrolname].ng-invalid',
  '[formcontrol].ng-invalid',
].join(', ');

const FOCUSABLE_SELECTOR = 'input, textarea, select, mat-select, [tabindex]';

export interface RevealInvalidOptions {
  expand?: () => void;
}

/**
 * Marks every control touched so its <mat-error> renders, then brings the first
 * invalid control inside `host` on screen and focuses it. On a phone the submit
 * button sits in a sticky bar at the bottom while the offending field is usually
 * scrolled off the top, so a silent early return looks like a dead button. The
 * field lands in the top quarter of the viewport so its error text stays clear of
 * that bar once focusing it opens the keyboard.
 * `host` is the <form> being submitted, or an element whose first <form> is it;
 * controls of any other form inside `host` (an embedded component with a form of
 * its own) are ignored. `expand` runs first so a collapsed section holding the
 * control can be opened.
 */
export function revealFirstInvalid(form: AbstractControl, host: HTMLElement | null | undefined, options: RevealInvalidOptions = {}): void {
  form.markAllAsTouched();
  options.expand?.();
  if (!host) return;
  setTimeout(() => {
    const target = findFirstInvalidElement(host);
    if (!target) return;
    const field = target.closest<HTMLElement>('mat-form-field') ?? target;
    field.style.scrollMarginTop = `${Math.round(window.innerHeight / 4)}px`;
    field.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
    const focusable = target.matches(FOCUSABLE_SELECTOR) ? target : target.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
    focusable?.focus({ preventScroll: true });
  });
}

/**
 * The first invalid control element in DOM order inside the form `host` denotes,
 * or, when the invalid control has no element of its own, the innermost invalid
 * group wrapping it; null when nothing invalid is rendered.
 */
export function findFirstInvalidElement(host: HTMLElement): HTMLElement | null {
  const scope = host.matches('form') ? host : host.querySelector('form');
  const inScope = (el: Element): boolean => !scope || el.closest('form') === scope;
  const control = Array.from(host.querySelectorAll<HTMLElement>(INVALID_CONTROL_SELECTOR)).find(inScope);
  if (control) return control;
  return Array.from(host.querySelectorAll<HTMLElement>('.ng-invalid:not(form)'))
    .find((el) => inScope(el) && !el.querySelector('.ng-invalid')) ?? null;
}
