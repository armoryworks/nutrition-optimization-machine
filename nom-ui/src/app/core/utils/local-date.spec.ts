import { parseLocalDateString, toLocalDateString } from './local-date';

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

describe('parseLocalDateString', () => {
  it('parses YYYY-MM-DD as local midnight on the same calendar day', () => {
    const parsed = parseLocalDateString('1971-04-11');
    expect(parsed.getFullYear()).toBe(1971);
    expect(parsed.getMonth()).toBe(3);
    expect(parsed.getDate()).toBe(11);
    expect(parsed.getHours()).toBe(0);
  });

  it('round-trips through toLocalDateString without drifting', () => {
    expect(toLocalDateString(parseLocalDateString('1990-11-23'))).toBe('1990-11-23');
  });

  it('ignores a trailing time component', () => {
    expect(toLocalDateString(parseLocalDateString('1990-11-23T00:00:00'))).toBe('1990-11-23');
  });
});
