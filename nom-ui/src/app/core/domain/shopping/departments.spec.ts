import { categorizeDepartment, DEPARTMENT_ORDER, DEPARTMENT_ICONS } from './departments';

describe('categorizeDepartment', () => {
  it('classifies common ingredients into the expected departments', () => {
    expect(categorizeDepartment('boneless chicken breast')).toBe('Meat & Seafood');
    expect(categorizeDepartment('whole milk')).toBe('Dairy & Eggs');
    expect(categorizeDepartment('roma tomato')).toBe('Produce');
    expect(categorizeDepartment('spaghetti')).toBe('Grains & Pasta');
    expect(categorizeDepartment('all-purpose flour')).toBe('Baking');
    expect(categorizeDepartment('smoked paprika')).toBe('Spices & Seasonings');
    expect(categorizeDepartment('olive oil')).toBe('Oils & Vinegars');
    expect(categorizeDepartment('frozen pizza')).toBe('Frozen');
  });

  it('uses word boundaries so substrings do not misclassify', () => {
    // "peach" contains "pea" but must not match the produce "peas" pattern early;
    // it should still land in Produce via the fruit pattern, not by accident.
    expect(categorizeDepartment('peach')).toBe('Produce');
    // "fish sauce" is a condiment even though it contains "fish"
    expect(categorizeDepartment('fish sauce')).toBe('Condiments & Sauces');
  });

  it('falls back to Other for unknown ingredients', () => {
    expect(categorizeDepartment('mystery ingredient')).toBe('Other');
  });

  it('has an icon for every ordered department', () => {
    for (const dept of DEPARTMENT_ORDER) {
      expect(DEPARTMENT_ICONS[dept]).toBeTruthy();
    }
  });
});
