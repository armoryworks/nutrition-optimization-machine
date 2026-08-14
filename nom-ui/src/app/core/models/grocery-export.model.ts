/**
 * Grocery export ("Send to…") contracts — see `GroceryExportController`.
 *
 * Provider identity is entirely server-driven: the UI never names a retailer,
 * it renders whatever `displayName`/`description` the operator's grocery
 * service reports. An empty provider list means the feature is unconfigured
 * and must stay invisible.
 */

/** How a destination consumes a list: share text, hand-off link, or a real cart. */
export type GroceryProviderKind = 'Text' | 'Link' | 'Cart';

/** Text-provider rendering styles. */
export type GroceryExportFormat = 'plain' | 'markdown' | 'csv';

export interface GroceryProviderInfo {
  key: string;
  displayName: string;
  kind: GroceryProviderKind;
  /** True when the destination needs a linked retailer account. */
  requiresConnection: boolean;
  /** False when the operator has not supplied credentials for this destination. */
  configured: boolean;
  /** True when this user already linked their account. */
  connected: boolean;
  description: string;
}

/** An item the destination could not match to a purchasable product. */
export interface GroceryUnmatchedItem {
  name: string;
  reason: string;
}

export interface GroceryExportResult {
  /** Logical outcome — the request itself returns 200 even on failure. */
  success: boolean;
  kind: GroceryProviderKind;
  url?: string;
  text?: string;
  addedCount?: number;
  unmatched: GroceryUnmatchedItem[];
  error?: string;
}

export interface GroceryExportOptions {
  provider: string;
  /** Text providers only; defaults to `plain` server-side. */
  format?: GroceryExportFormat;
  /** Leave checked-off items out of the export. Defaults to true server-side. */
  excludeChecked?: boolean;
}

/**
 * One line of an ad-hoc export. `quantity`/`unit` stay unset when the caller
 * only has display text for the amount — `note` carries that instead, and
 * inventing a number would mislead the retailer's matcher.
 */
export interface GroceryExportItem {
  name: string;
  quantity?: number;
  unit?: string;
  /** Retail package the amount rounds up to, e.g. "2 × 5 lb bag". */
  packageHint?: string;
  /** Store department / aisle grouping. */
  category?: string;
  note?: string;
}

/**
 * Body of `POST /GroceryExport/items` — exports a list the client is holding
 * rather than a persisted one. Blank-name lines are dropped server-side, and
 * an empty payload comes back `success: false` without contacting the retailer.
 */
export interface GroceryExportItemsRequest {
  provider: string;
  /** Text providers only; defaults to `plain` server-side. */
  format?: GroceryExportFormat;
  /** Omit to accept the server's dated default title. */
  title?: string;
  items: GroceryExportItem[];
}

/** Response of `POST /GroceryExport/connect/{provider}` — where to send the browser. */
export interface GroceryConnectStart {
  url: string;
}

export interface GroceryStore {
  id: string;
  name: string;
  address: string;
}

export interface GroceryStoreSelection {
  locationId: string;
  locationName?: string;
}
