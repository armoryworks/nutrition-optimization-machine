import { HouseholdShoppingItem } from '../../models/household-shopping.model';
import { ShoppingDepartment, ShoppingItem } from './shopping-types';
import { DEPARTMENT_ICONS, DEPARTMENT_ORDER, categorizeDepartment } from './departments';

export const ADDED_BY_YOU = 'Added by you';
export const ADDED_BY_YOU_ICON = 'playlist_add';

export function adhocCheckKey(id: number): string {
  return `item-${id}`;
}

/**
 * The aisle for an item a member typed in: its name (or the linked food's
 * group) decides when it matches a known department; otherwise it waits in
 * "Added by you" instead of being buried in "Other".
 */
export function adhocDepartment(item: HouseholdShoppingItem): string {
  const dept = categorizeDepartment(item.name, item.department);
  return dept === 'Other' ? ADDED_BY_YOU : dept;
}

export function toShoppingItem(item: HouseholdShoppingItem): ShoppingItem {
  const quantity = item.quantity ?? 0;
  const unit = (item.unit ?? '').trim();
  return {
    ingredientId: item.ingredientId ?? null,
    name: item.name,
    portions: quantity > 0 || unit ? [{ quantity, unit }] : [],
    department: adhocDepartment(item),
    checkKey: adhocCheckKey(item.id),
    baseMassG: 0,
    baseVolumeMl: 0,
    baseCount: quantity,
    retailPackage: null,
    retailPackageCount: 0,
    sourceRecipes: [],
    source: 'adhoc',
    adhocId: item.id,
  };
}

/**
 * Lays household-added items over the plan-derived departments: a matching
 * aisle gains the line (created in store order when absent), and everything
 * else collects in a trailing "Added by you" section. The derived lines are
 * never touched.
 */
export function mergeAdhocItems(
  departments: ShoppingDepartment[],
  items: HouseholdShoppingItem[],
): ShoppingDepartment[] {
  if (items.length === 0) return departments;

  const result = departments.map((d) => ({ ...d, items: [...d.items] }));
  const extra: ShoppingItem[] = [];

  for (const raw of items) {
    const item = toShoppingItem(raw);
    if (item.department === ADDED_BY_YOU) {
      extra.push(item);
      continue;
    }
    let dept = result.find((d) => d.name === item.department);
    if (!dept) {
      dept = { name: item.department, icon: DEPARTMENT_ICONS[item.department] ?? 'category', items: [] };
      const order = DEPARTMENT_ORDER.indexOf(item.department);
      const at = result.findIndex((d) => {
        const i = DEPARTMENT_ORDER.indexOf(d.name);
        return i === -1 || i > order;
      });
      if (at === -1) result.push(dept);
      else result.splice(at, 0, dept);
    }
    dept.items.push(item);
    dept.items.sort((a, b) => a.name.localeCompare(b.name));
  }

  if (extra.length > 0) {
    extra.sort((a, b) => a.name.localeCompare(b.name));
    result.push({ name: ADDED_BY_YOU, icon: ADDED_BY_YOU_ICON, items: extra });
  }

  return result;
}
