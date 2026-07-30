import {
  getUnitInfo,
  toWeightDisplay,
  toVolumeDisplay,
  toLiquidDisplay,
  formatQuantity,
  pluralizePackage,
} from './unit-conversion';

describe('unit conversion', () => {
  it('maps known units to categories and base factors', () => {
    expect(getUnitInfo('cup')).toEqual({ category: 'volume', toBase: 236.588 });
    expect(getUnitInfo('Pound')).toEqual({ category: 'mass', toBase: 453.592 });
    expect(getUnitInfo('unknown-unit').category).toBe('other');
  });

  it('picks sensible weight displays', () => {
    expect(toWeightDisplay(453.592)).toEqual({ quantity: 1, unit: 'lb' });
    expect(toWeightDisplay(56.7).unit).toBe('oz');
  });

  it('picks sensible volume displays', () => {
    expect(toVolumeDisplay(236.588)).toEqual({ quantity: 1, unit: 'cup' });
    expect(toVolumeDisplay(14.787).unit).toBe('tbsp');
    expect(toVolumeDisplay(4.9).unit).toBe('tsp');
  });

  it('scales liquid displays through fl oz, qt, and gal', () => {
    expect(toLiquidDisplay(29.574).unit).toBe('fl oz');
    expect(toLiquidDisplay(29.574 * 40).unit).toBe('qt');
    expect(toLiquidDisplay(29.574 * 200).unit).toBe('gal');
  });

  it('formats cooking-friendly fractions', () => {
    expect(formatQuantity(2)).toBe('2');
    expect(formatQuantity(0.25)).toBe('1/4');
    expect(formatQuantity(1.5)).toBe('1 1/2');
    expect(formatQuantity(0.4)).toBe('0.4');
  });

  it('pluralizes package names', () => {
    expect(pluralizePackage('can', 2)).toBe('cans');
    expect(pluralizePackage('box', 3)).toBe('boxes');
    expect(pluralizePackage('loaf', 2)).toBe('loaves');
    expect(pluralizePackage('bottle', 1)).toBe('bottle');
  });
});
