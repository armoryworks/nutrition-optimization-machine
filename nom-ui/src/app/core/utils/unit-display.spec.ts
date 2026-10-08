import { formatUnit } from './unit-display';

describe('formatUnit', () => {
  it('lowercases every unit name the same way, short or long', () => {
    expect(formatUnit('Cup', 2)).toBe('cups');
    expect(formatUnit('Cup', 1)).toBe('cup');
    expect(formatUnit('Tablespoon', 2)).toBe('tablespoons');
    expect(formatUnit('Can', 1)).toBe('can');
    expect(formatUnit('Fluid Ounce', 8)).toBe('fluid ounces');
  });

  it('pluralizes units ending in ch, sh, x or z with es', () => {
    expect(formatUnit('Pinch', 2)).toBe('pinches');
    expect(formatUnit('Dash', 3)).toBe('dashes');
  });

  it('leaves abbreviations and unpluralized units alone', () => {
    expect(formatUnit('mL', 1)).toBe('mL');
    expect(formatUnit('Dozen', 2)).toBe('dozen');
    expect(formatUnit(null, 2)).toBe('');
  });
});
