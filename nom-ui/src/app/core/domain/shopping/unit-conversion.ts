import { RetailPackagingResponse } from '../../models/retail-packaging-response.model';
import { ShoppingPortion } from './shopping-types';

// Unit conversion: recipe measurements → base units (ml for volume, g for mass)
export type UnitCategory = 'volume' | 'mass' | 'count' | 'other';

export interface UnitInfo {
  category: UnitCategory;
  toBase: number;
}

export const UNIT_INFO: Record<string, UnitInfo> = {
  teaspoon: { category: 'volume', toBase: 4.929 },
  tablespoon: { category: 'volume', toBase: 14.787 },
  'fluid ounce': { category: 'volume', toBase: 29.574 },
  cup: { category: 'volume', toBase: 236.588 },
  milliliter: { category: 'volume', toBase: 1 },
  liter: { category: 'volume', toBase: 1000 },
  gram: { category: 'mass', toBase: 1 },
  ounce: { category: 'mass', toBase: 28.3495 },
  pound: { category: 'mass', toBase: 453.592 },
  kilogram: { category: 'mass', toBase: 1000 },
  piece: { category: 'count', toBase: 1 },
  each: { category: 'count', toBase: 1 },
  dozen: { category: 'count', toBase: 12 },
};

export function getUnitInfo(measurement: string): UnitInfo {
  return UNIT_INFO[measurement.toLowerCase()] ?? { category: 'other', toBase: 1 };
}

/** Round to the nearest fraction (e.g., nearest 1/4 for denominator=4) */
export function roundToFraction(qty: number, denominator: number): number {
  return Math.round(qty * denominator) / denominator;
}

// ---- Ingredient classification for shopping-friendly display ----

/** Is this ingredient a pourable liquid? (Display in fl oz) */
export function isLiquid(name: string): boolean {
  const n = name.toLowerCase();
  return /\b(milk|cream|buttermilk|half and half|broth|stock|juice|water|coconut milk|coconut cream|oil|vinegar|wine|beer|soda|kombucha|soy sauce|fish sauce|worcestershire|hot sauce|sriracha|teriyaki|hoisin|oyster sauce|honey|maple syrup|molasses|agave|lemon juice|lime juice|mirin|sake)\b/.test(
    n,
  );
}

/** Is this a fresh herb typically sold by the bunch? */
export function isFreshHerb(name: string, department: string): boolean {
  if (department !== 'Produce') return false;
  return /\b(cilantro|parsley|basil|mint|dill|rosemary|thyme|chives|oregano|sage|tarragon)\b/.test(
    name.toLowerCase(),
  );
}

/** Approximate density (g per ml) for converting recipe volume → shopping weight */
export function getIngredientDensity(name: string): number {
  const n = name.toLowerCase();
  if (/cheese|parmesan|mozzarella|cheddar|feta|gouda|brie|ricotta|cream cheese/.test(n)) return 0.5;
  if (/yogurt|sour cream|kefir/.test(n)) return 1.03;
  if (/butter|ghee|margarine/.test(n)) return 0.91;
  if (/peanut butter|almond butter|nutella|tahini/.test(n)) return 1.1;
  if (/flour/.test(n)) return 0.53;
  if (/sugar/.test(n)) return 0.85;
  if (/rice|quinoa|couscous|barley|oat|granola|cereal|grain/.test(n)) return 0.75;
  if (/nut|almond|walnut|pecan|cashew|pistachio|seed/.test(n)) return 0.55;
  if (/spinach|kale|arugula|lettuce|greens|cabbage/.test(n)) return 0.15;
  if (/ginger/.test(n)) return 0.7;
  if (/cocoa|cornstarch|baking/.test(n)) return 0.55;
  if (/breadcrumb|panko|cracker/.test(n)) return 0.45;
  if (/olive|caper|pickle/.test(n)) return 0.65;
  return 0.6;
}

/** Convert grams to the best weight display (oz or lb) */
export function toWeightDisplay(grams: number): ShoppingPortion {
  if (grams >= 453.592) {
    return { quantity: roundToFraction(grams / 453.592, 4) || 0.25, unit: 'lb' };
  }
  return { quantity: roundToFraction(grams / 28.3495, 2) || 0.5, unit: 'oz' };
}

/** Convert ml to the best recipe-friendly volume display (tsp, tbsp, cup) */
export function toVolumeDisplay(ml: number): ShoppingPortion {
  if (ml >= 236.588) {
    return { quantity: roundToFraction(ml / 236.588, 4) || 0.25, unit: 'cup' };
  }
  if (ml >= 14.787) {
    return { quantity: roundToFraction(ml / 14.787, 4) || 0.25, unit: 'tbsp' };
  }
  return { quantity: roundToFraction(ml / 4.929, 4) || 0.25, unit: 'tsp' };
}

/** Convert ml to the best liquid display (fl oz or qt or gal) */
export function toLiquidDisplay(ml: number): ShoppingPortion {
  const flOz = ml / 29.574;
  if (flOz >= 128) return { quantity: roundToFraction(flOz / 128, 4) || 0.25, unit: 'gal' };
  if (flOz >= 32) return { quantity: roundToFraction(flOz / 32, 4) || 0.25, unit: 'qt' };
  return { quantity: roundToFraction(flOz, 2) || 0.5, unit: 'fl oz' };
}

/**
 * Find the best retail packaging match for an ingredient name.
 * Matches by longest pattern (most specific). No sizeCategory filter —
 * the caller converts recipe units to the package's category via density.
 */
export function findRetailPackage(
  name: string,
  packages: RetailPackagingResponse[],
): RetailPackagingResponse | null {
  const lower = name.toLowerCase();
  let bestMatch: RetailPackagingResponse | null = null;
  let bestLen = 0;

  for (const pkg of packages) {
    const pattern = pkg.ingredientPattern.toLowerCase();
    if (lower.includes(pattern) && pattern.length > bestLen) {
      bestMatch = pkg;
      bestLen = pattern.length;
    }
  }
  return bestMatch;
}

/** Pluralize a package name: "can" → "cans", "box" → "boxes", etc. */
export function pluralizePackage(name: string, count: number): string {
  if (count <= 1) return name;
  const n = name.toLowerCase();
  if (n === 'box') return 'boxes';
  if (n === 'bunch') return 'bunches';
  if (n === 'loaf') return 'loaves';
  if (
    n.endsWith('ch') ||
    n.endsWith('sh') ||
    n.endsWith('ss') ||
    n.endsWith('x') ||
    n.endsWith('z')
  )
    return name + 'es';
  return name + 's';
}

/**
 * Format a retail packaging portion for display.
 * 1 package  → "16.9 fl oz bottle"   (omit count of 1)
 * 2 packages → "2 × 8 oz boxes"      (× separator prevents number collision)
 */
export function formatRetailPortion(
  pkg: RetailPackagingResponse,
  pkgCount: number,
): ShoppingPortion {
  const pkgLabel = `${pkg.packageSize} ${pkg.packageSizeUnit} ${pluralizePackage(pkg.packageName, pkgCount)}`;
  if (pkgCount <= 1) {
    return { quantity: 0, unit: pkgLabel };
  }
  return { quantity: 0, unit: `${pkgCount} × ${pkgLabel}` };
}

/** Is this a small-volume item (spice, seasoning, extract) that should stay in tsp/tbsp? */
export function isSmallVolumeItem(name: string): boolean {
  return /\b(salt|pepper|paprika|cumin|cinnamon|turmeric|oregano|chili powder|cayenne|nutmeg|coriander|garlic powder|onion powder|ginger powder|allspice|cardamom|cloves|fennel seed|mustard powder|saffron|baking soda|baking powder|cream of tartar|vanilla extract|vanilla|almond extract|extract|seasoning|spice)\b/i.test(
    name,
  );
}

/** Convert ml to the best small-volume display (tsp or tbsp) */
export function toSmallVolumeDisplay(ml: number): ShoppingPortion {
  const tbsp = ml / 14.787;
  if (tbsp >= 1) {
    return { quantity: roundToFraction(tbsp, 4) || 0.25, unit: 'tbsp' };
  }
  const tsp = ml / 4.929;
  return { quantity: roundToFraction(tsp, 4) || 0.25, unit: 'tsp' };
}

/** Format a quantity with cooking-friendly fractions (1/4, 1/3, 1/2, 2/3, 3/4). */
export function formatQuantity(qty: number): string {
  if (qty === Math.floor(qty)) return qty.toString();
  const frac = qty - Math.floor(qty);
  const whole = Math.floor(qty);
  if (Math.abs(frac - 0.25) < 0.01) return whole ? `${whole} 1/4` : '1/4';
  if (Math.abs(frac - 0.33) < 0.02) return whole ? `${whole} 1/3` : '1/3';
  if (Math.abs(frac - 0.5) < 0.01) return whole ? `${whole} 1/2` : '1/2';
  if (Math.abs(frac - 0.67) < 0.02) return whole ? `${whole} 2/3` : '2/3';
  if (Math.abs(frac - 0.75) < 0.01) return whole ? `${whole} 3/4` : '3/4';
  return qty.toFixed(1);
}
