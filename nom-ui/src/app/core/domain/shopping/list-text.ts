import { GroceryExportItem } from '../../models/grocery-export.model';

/** Plain-text shopping list: the title, then each department's lines under its name. */
export function shoppingListText(
  title: string,
  items: GroceryExportItem[],
  line: (item: GroceryExportItem) => string,
): string {
  const lines = [title];
  let category: string | undefined;
  for (const item of items) {
    if (item.category !== category) {
      category = item.category;
      lines.push('', `${category}:`);
    }
    lines.push(`- ${line(item)}`);
  }
  return lines.join('\n');
}

/**
 * What the share sheet receives. The title already heads the text, so it is not
 * sent again as `title`: share targets that print both showed the header twice.
 */
export function shoppingListShareData(
  title: string,
  items: GroceryExportItem[],
  line: (item: GroceryExportItem) => string,
): ShareData {
  return { text: shoppingListText(title, items, line) };
}
