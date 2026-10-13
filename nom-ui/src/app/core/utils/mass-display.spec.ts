import { formatMass, massAside } from './mass-display';

describe('mass display', () => {
  it('shows grams and kilograms in metric', () => {
    expect(formatMass(125, 'metric')).toBe('125 g');
    expect(formatMass(2.3, 'metric')).toBe('2.3 g');
    expect(formatMass(1250, 'metric')).toBe('1.25 kg');
  });

  it('shows ounces and pounds in imperial', () => {
    expect(formatMass(125, 'imperial')).toBe('4.4 oz');
    expect(formatMass(454, 'imperial')).toBe('1 lb');
    expect(formatMass(0.5, 'imperial')).toBe('< 0.1 oz');
  });

  it('shows nothing without a mass', () => {
    expect(formatMass(null, 'metric')).toBe('');
    expect(formatMass(0, 'imperial')).toBe('');
  });

  it('skips lines already weighed in the reader system', () => {
    expect(massAside(200, 'Gram', 'metric')).toBe('');
    expect(massAside(200, 'Gram', 'imperial')).toBe('7.1 oz');
    expect(massAside(453.6, 'Pound', 'imperial')).toBe('');
    expect(massAside(453.6, 'Pound', 'metric')).toBe('454 g');
    expect(massAside(125, 'Cup', 'metric')).toBe('125 g');
  });
});
