/** Department-based default shelf life in days. */
export const SHELF_LIFE_DEFAULTS: Record<string, number> = {
  produce: 5,
  'meat & seafood': 3,
  'dairy & eggs': 10,
  bakery: 5,
  'grains & pasta': 180,
  'canned & jarred': 365,
  'condiments & sauces': 90,
  'spices & seasonings': 365,
  'oils & vinegars': 180,
  baking: 180,
  frozen: 90,
  beverages: 30,
  other: 90,
};

export function shelfLifeDaysFor(department: string): number {
  return SHELF_LIFE_DEFAULTS[department.toLowerCase()] ?? 90;
}
