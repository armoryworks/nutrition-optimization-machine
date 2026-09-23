export interface MeasurementOption {
  id: number;
  name: string;
  symbol: string;
  categoryId: number;
  categoryName: string;
}

const FOOD_CATEGORIES = new Set(['Mass', 'Volume', 'Count']);

const COMMON_UNIT_ORDER = [
  'Cup', 'Tablespoon', 'Teaspoon', 'Fluid Ounce', 'Milliliter', 'Liter',
  'Gram', 'Kilogram', 'Ounce', 'Pound', 'Piece', 'Slice', 'Can', 'Dozen',
];

export function foodUnits(all: MeasurementOption[]): MeasurementOption[] {
  return all
    .filter(m => FOOD_CATEGORIES.has(m.categoryName))
    .sort((a, b) => {
      const ai = COMMON_UNIT_ORDER.indexOf(a.name);
      const bi = COMMON_UNIT_ORDER.indexOf(b.name);
      if (ai !== -1 || bi !== -1) {
        return (ai === -1 ? COMMON_UNIT_ORDER.length : ai) - (bi === -1 ? COMMON_UNIT_ORDER.length : bi);
      }
      return a.name.localeCompare(b.name);
    });
}
