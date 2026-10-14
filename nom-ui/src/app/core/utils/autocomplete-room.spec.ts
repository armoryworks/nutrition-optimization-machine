import { KEYBOARD_SETTLE_MS, makeRoomForAutocomplete } from './autocomplete-room';

describe('makeRoomForAutocomplete', () => {
  let input: HTMLInputElement;
  let scrollIntoView: ReturnType<typeof vi.fn>;
  let phone: boolean;
  const originalMatchMedia = window.matchMedia;

  beforeEach(() => {
    vi.useFakeTimers();
    phone = true;
    window.matchMedia = ((query: string) => ({ matches: phone, media: query })) as unknown as typeof window.matchMedia;
    input = document.createElement('input');
    input.classList.add('mat-mdc-autocomplete-trigger');
    scrollIntoView = vi.fn();
    input.scrollIntoView = scrollIntoView as unknown as typeof input.scrollIntoView;
    document.body.appendChild(input);
  });

  afterEach(() => {
    vi.useRealTimers();
    window.matchMedia = originalMatchMedia;
    input.remove();
  });

  it('scrolls a focused autocomplete field to the top on a phone once the keyboard settles', () => {
    input.focus();
    makeRoomForAutocomplete(input, window);

    expect(scrollIntoView).not.toHaveBeenCalled();
    vi.advanceTimersByTime(KEYBOARD_SETTLE_MS);
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'start', behavior: 'smooth' });
  });

  it('leaves wider screens alone', () => {
    phone = false;
    input.focus();
    makeRoomForAutocomplete(input, window);

    vi.advanceTimersByTime(KEYBOARD_SETTLE_MS);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it('ignores fields without an autocomplete', () => {
    input.classList.remove('mat-mdc-autocomplete-trigger');
    input.focus();
    makeRoomForAutocomplete(input, window);

    vi.advanceTimersByTime(KEYBOARD_SETTLE_MS);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it('does not scroll once focus has moved on', () => {
    input.focus();
    makeRoomForAutocomplete(input, window);
    input.blur();

    vi.advanceTimersByTime(KEYBOARD_SETTLE_MS);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });
});
