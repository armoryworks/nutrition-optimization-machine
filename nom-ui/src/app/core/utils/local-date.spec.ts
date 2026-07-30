import { toLocalDateString } from './local-date';

describe('toLocalDateString', () => {
  it('formats dates in local time as YYYY-MM-DD', () => {
    expect(toLocalDateString(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(toLocalDateString(new Date(2026, 11, 31))).toBe('2026-12-31');
  });

  it('does not shift the date near local midnight (the toISOString bug)', () => {
    const lateEvening = new Date(2026, 6, 29, 23, 30);
    expect(toLocalDateString(lateEvening)).toBe('2026-07-29');
  });
});
