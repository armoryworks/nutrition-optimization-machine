# Grocery Export Integration

NOM can send a shopping list somewhere useful — the phone's share sheet, a
delivery marketplace, or a retailer's own cart. Like recipe scraping, **NOM
ships with none of that built in**: the retailer-specific code lives in an
external, operator-provided *grocery service*, and NOM talks to it through the
neutral contract below.

Why the split:

- **Partner terms are per-operator.** Instacart's developer agreement, Kroger's
  API terms, and both companies' branding rules bind the person holding the
  keys. Shared credentials can't ship in an open repo.
- **Coverage differs by deployment.** One operator wants Instacart, another only
  wants "copy to clipboard", another has a regional chain nobody else does.
- **Zero-config is a supported state.** With no service configured, the
  provider list comes back empty and the UI hides the feature entirely.

The reference implementation is the **private** `armoryworks/nom-grocery` repo.
Anything that speaks this contract works.

## Configuring NOM

`nom-api` `appsettings.json` (or environment variables):

```json
"GroceryExport": {
  "BaseUrl": "http://nom-grocery:8080",
  "ApiKey": "the-shared-secret-configured-on-the-grocery-service",
  "InstanceId": "nommeal-prod",
  "TimeoutSeconds": 45
}
```

`GroceryExport__BaseUrl` / `GroceryExport__ApiKey` as env vars in Docker.
`InstanceId` is optional and is sent as `X-Instance-Id`; services that gate on
an instance allowlist use it to serve only known deployments.

Encryption note: retailer OAuth tokens are stored in `shopping.GroceryConnection`
encrypted with NOM's `Encryption:Key`. Set that to a stable value before
connecting any retailer account, or existing connections become unreadable when
it changes (users then simply reconnect).

## The contract

Every `/api/*` call carries `X-Api-Key` (plus `X-Instance-Id` when configured).

### `GET /api/providers`

```json
[
  { "key": "text", "displayName": "Share or copy", "kind": "Text",
    "requiresConnection": false, "configured": true,
    "description": "Send the list to any app on your device." }
]
```

`kind` drives the client: `Text` → hand `text` to the share sheet/clipboard;
`Link` → open `url`; `Cart` → items were pushed to the user's retailer account.
`configured: false` means the operator supplied no credentials for it — NOM shows
it greyed out rather than failing at click time.

### `POST /api/export`

```json
{
  "provider": "instacart",
  "title": "Week of Aug 17",
  "format": "plain",
  "items": [
    { "name": "all-purpose flour", "quantity": 5, "unit": "lb",
      "packageHint": "5 lb bag", "category": "Baking", "note": null, "upc": null }
  ],
  "connection": { "accessToken": "…", "refreshToken": "…", "locationId": "70100001" }
}
```

Response:

```json
{ "success": true, "kind": "Link", "url": "https://…", "text": null,
  "addedCount": null, "unmatched": [], "error": null }
```

Contract rules an implementation must honor:

1. **Never drop an item silently.** Anything unmatched comes back in
   `unmatched[]` with a reason; NOM surfaces it to the user.
2. **Never invent quantities.** An item with no quantity exports as a bare name.
3. **Preserve aisle order.** `category` grouping should survive text exports.
4. **Report unconfigured, don't fail.** Providers advertise `configured: false`
   rather than erroring when called.

### Connection endpoints (only for `kind: "Cart"`)

```
POST /api/connect/authorize-url   { provider, redirectUri, state } -> { url }
POST /api/connect/exchange        { provider, code, redirectUri }  -> { accessToken, refreshToken, expiresAt }
POST /api/connect/exchange        { provider, refreshToken }       -> refreshed tokens
GET  /api/stores?provider=&postalCode=                             -> [{ id, name, address }]
```

NOM issues the `state` (it encodes the person id plus a nonce), stores it
encrypted, and rejects any callback whose state doesn't match.

## What NOM does around it

- **Package hints.** Before exporting, NOM looks up its `RetailPackaging` data
  and attaches a hint like `5 lb bag`, which is what makes retailer product
  matching land on a sensible size instead of the first search hit.
- **Checked items** are excluded by default (`excludeChecked`).
- **Tokens** live only in `shopping.GroceryConnection`, encrypted; they are never
  returned to the browser. Disconnecting hard-deletes the row rather than
  soft-deleting it.
- **Callback** lands on `GET /api/GroceryExport/callback/{provider}` and
  redirects back into the app with `?connected=ok|failed`.

## NOM's own endpoints

```
GET    /api/GroceryExport/providers
POST   /api/GroceryExport/items                   { provider, format?, title?, items[] }
POST   /api/GroceryExport/list/{shoppingListId}   { provider, format?, excludeChecked? }
POST   /api/GroceryExport/connect/{provider}?returnUrl=…
GET    /api/GroceryExport/callback/{provider}      (retailer redirect target)
GET    /api/GroceryExport/stores/{provider}?postalCode=…
PUT    /api/GroceryExport/stores/{provider}        { locationId, locationName }
DELETE /api/GroceryExport/connect/{provider}
```

### Which export endpoint to use

`/items` takes the lines the client is displaying and is what the shopping view
uses: that view is a live projection over the meal plan, pantry, and retail
packaging, so there is usually no `ShoppingList` row behind it. `/list/{id}`
exports a persisted list and applies NOM's own package-hint lookup server-side;
use it when a saved list genuinely exists.

## Running the reference service

```bash
git clone git@github.com:armoryworks/nom-grocery.git
cd nom-grocery
docker build -t nom-grocery .
docker run -d --name nom-grocery -p 8080:8080 \
  -e ApiKey=$(openssl rand -hex 24) \
  -e Grocery__Instacart__ApiKey=<partner key> \
  nom-grocery
```

See that repo's README for the provider matrix, credentials, and current
verification status of each integration.
