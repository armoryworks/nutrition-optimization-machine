# Angular Routing Redirects

## Overview

This document outlines the routing redirects implemented in the NOM Angular application to handle singular/plural path variations and common user navigation patterns.

## Redirect Structure

All redirects are defined in `src/app/app.routes.ts` and are placed before the lazy-loaded feature routes to ensure they are processed first.

## Singular to Plural Redirects

These redirects handle cases where users might navigate to singular versions of plural paths:

| Singular Path    | Redirects To | Purpose                                          |
| ---------------- | ------------ | ------------------------------------------------ |
| `recipe`         | `recipes`    | Redirects single recipe to recipes list          |
| `meal-plans`     | `meal-plan`  | Redirects plural meal-plans to meal-plan feature |
| `shopping-list`  | `shopping`   | Redirects shopping-list to shopping feature      |
| `shopping-lists` | `shopping`   | Redirects shopping-lists to shopping feature     |
| `households`     | `household`  | Redirects plural households to household feature |
| `users`          | `user`       | Redirects plural users to user feature           |
| `admin-panel`    | `admin`      | Redirects admin-panel to admin feature           |
| `curations`      | `curation`   | Redirects plural curations to curation feature   |

## Additional Common Redirects

These redirects handle common user navigation patterns and shortcuts:

| Short Path  | Redirects To    | Purpose                       |
| ----------- | --------------- | ----------------------------- |
| `profile`   | `user/profile`  | Quick access to user profile  |
| `settings`  | `user/settings` | Quick access to user settings |
| `dashboard` | `home`          | Alternative dashboard path    |
| `plans`     | `curated-plans` | Shortcut to curated plans     |
| `plan`      | `curated-plans` | Single plan to curated plans  |

## Implementation Details

### Redirect Configuration

All redirects use the following pattern:

```typescript
{ path: 'singular-path', redirectTo: 'plural-path', pathMatch: 'full' }
```

- `pathMatch: 'full'` ensures exact path matching
- Redirects are processed before lazy-loaded routes
- No authentication guards are applied to redirects

### Route Order

1. **Eager-loaded routes** - Immediate access routes
2. **Redirects** - Path variations and shortcuts
3. **Lazy-loaded feature routes** - Feature modules
4. **Default and wildcard routes** - Must be last

### Benefits

- **User Experience**: Users can navigate using intuitive paths
- **SEO Friendly**: Consistent URL structure
- **Maintenance**: Centralized redirect management
- **Flexibility**: Easy to add new redirects as needed

## Adding New Redirects

To add a new redirect:

1. Add the redirect rule in `src/app/app.routes.ts` in the redirects section
2. Use the pattern: `{ path: 'old-path', redirectTo: 'new-path', pathMatch: 'full' }`
3. Place redirects before lazy-loaded routes
4. Test the redirect works as expected
5. Update this documentation

## Testing Redirects

After adding redirects:

1. Run `ng build` to ensure no compilation errors
2. Test navigation in the browser
3. Verify redirects work with and without trailing slashes
4. Check that deep linking still works correctly

## Special Cases

### Communication/Messaging Routes

Both `/communication` and `/messaging` paths load the same communication feature module to support both URL patterns:

```typescript
// Both paths supported for communication feature
{
  path: 'messaging',
  loadChildren: () => import('./communication/communication.routes').then(m => m.COMMUNICATION_ROUTES),
  canActivate: [AuthGuard]
},
{
  path: 'communication',
  loadChildren: () => import('./communication/communication.routes').then(m => m.COMMUNICATION_ROUTES),
  canActivate: [AuthGuard]
}
```

## Current Redirect Rules

```typescript
// Redirects for singular versions of plural paths
{ path: 'recipe', redirectTo: 'recipes', pathMatch: 'full' },
{ path: 'meal-plans', redirectTo: 'meal-plan', pathMatch: 'full' },
{ path: 'shopping-list', redirectTo: 'shopping', pathMatch: 'full' },
{ path: 'shopping-lists', redirectTo: 'shopping', pathMatch: 'full' },
{ path: 'households', redirectTo: 'household', pathMatch: 'full' },
{ path: 'users', redirectTo: 'user', pathMatch: 'full' },
{ path: 'admin-panel', redirectTo: 'admin', pathMatch: 'full' },
{ path: 'curations', redirectTo: 'curation', pathMatch: 'full' },

// Additional common redirects
{ path: 'profile', redirectTo: 'user/profile', pathMatch: 'full' },
{ path: 'settings', redirectTo: 'user/settings', pathMatch: 'full' },
{ path: 'dashboard', redirectTo: 'home', pathMatch: 'full' },
{ path: 'plans', redirectTo: 'curated-plans', pathMatch: 'full' },
{ path: 'plan', redirectTo: 'curated-plans', pathMatch: 'full' },
```
