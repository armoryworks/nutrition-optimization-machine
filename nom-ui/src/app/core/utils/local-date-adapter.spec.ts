import { TestBed } from '@angular/core/testing';
import { LocalDateAdapter } from './local-date-adapter';

describe('LocalDateAdapter', () => {
  let adapter: LocalDateAdapter;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [LocalDateAdapter] });
    adapter = TestBed.inject(LocalDateAdapter);
  });

  it('reads a typed YYYY-MM-DD as that local calendar day', () => {
    const date = adapter.parse('1980-10-22')!;
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([1980, 9, 22]);
    expect(date.getHours()).toBe(0);
  });

  it('rejects an ISO-shaped string that is not a real date', () => {
    expect(adapter.isValid(adapter.parse('1980-13-45')!)).toBe(false);
  });

  it('still accepts the locale formats the native adapter understands', () => {
    const date = adapter.parse('10/22/1980')!;
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([1980, 9, 22]);
    expect(adapter.parse('')).toBeNull();
    expect(adapter.isValid(adapter.parse('22/10/1980')!)).toBe(false);
  });
});
