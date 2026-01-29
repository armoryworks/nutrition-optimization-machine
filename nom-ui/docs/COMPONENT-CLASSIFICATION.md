# NOM Component Classification

This document maps every component to its appropriate stereotype based on actual UI analysis from screenshots.

## Stereotype Definitions

| Stereotype | Description | Key Classes |
|------------|-------------|-------------|
| **Landing** | Public pages, hero sections | `.nom-landing`, `.nom-landing__hero` |
| **Dashboard** | Overview pages with cards/stats | `.nom-dashboard`, `.nom-dashboard__grid`, `.nom-dashboard__card` |
| **Form-Card** | Centered create/edit forms | `.nom-form--card`, `.nom-form__card` |
| **Form-Full** | Full-width multi-section forms | `.nom-form`, `.nom-form__section` |
| **Detail** | Item view with sections | `.nom-detail`, `.nom-detail__section` |
| **Search** | Browse/filter with results | `.nom-search`, `.nom-search__filters`, `.nom-search__results` |
| **Master-Detail** | Split list + detail view | `.nom-master-detail`, `.nom-master-panel`, `.nom-detail-panel` |
| **Wizard** | Multi-step flows | `.nom-wizard`, `.nom-wizard__step` |
| **Calendar** | Date-based views | `.nom-calendar`, `.nom-calendar__grid` |
| **Modal** | Dialog overlays | `.nom-modal`, `.nom-modal__content` |
| **Inline** | Embedded components | Component-specific, minimal |

---

## Public Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `home` | **Landing** | 01-home.png | Hero with icons and CTAs |
| `about` | **Landing** | 02-about.png | Informational page |

---

## Auth Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `register` | **Form-Card** | 03-auth-register.png | Centered registration |
| `login` | **Form-Card** | - | Centered login |
| `forgot-password` | **Form-Card** | 04-auth-forgot-password.png | Centered form |
| `reset-password` | **Form-Card** | 05-auth-reset-password.png | Centered form |
| `confirm-email` | **Form-Card** | 06-auth-confirm-email.png | Centered message |
| `send-confirmation` | **Form-Card** | 07-auth-send-confirmation.png | Centered form |
| `login-popover` | **Modal** | - | Login overlay |

---

## User Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `recipe-author-dashboard` | **Dashboard** | 10-user-dashboard.png | Stats + dual grid sections |
| `privacy-settings` | **Form-Full** | 11-user-privacy-settings.png | Settings sections |
| `edit-profile` | **Form-Full** | 12-user-edit-profile.png | Profile form |
| `update-info` | **Form-Full** | 13-user-update-info.png | Account form |
| `update-two-factor` | **Form-Full** | 14-user-update-two-factor.png | Security settings |

---

## Household Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `household-dashboard` | **Dashboard** | 20-household-dashboard.png | Stats pill + card grid |
| `household-create` | **Form-Card** | 21-household-create.png | Centered create |
| `household-edit` | **Form-Full** | - | Edit sections |
| `household-detail` | **Detail** | - | Household view |
| `household-invite` | **Form-Card** | - | Centered invite |
| `household-invite-refactored` | **Form-Card** | - | Centered invite |
| `household-join` | **Form-Card** | - | Join form |
| `household-settings` | **Form-Full** | - | Settings sections |

---

## Shopping Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `shopping-dashboard` | **Dashboard** | 30-shopping-dashboard.png | Stats + card grid |
| `shopping-create` | **Form-Card** | 31-shopping-create.png | Centered create |
| `shopping-list` | **Master-Detail** | - | Items + detail |
| `shopping-edit` | **Form-Full** | - | Edit form |
| `shopping-detail` | **Detail** | - | List view |
| `shopping-item-editor` | **Modal** | - | Edit item |
| `shopping-item-form` | **Inline** | - | Item fields |
| `shopping-bulk-editor` | **Form-Full** | - | Bulk table |
| `shopping-list-export` | **Modal** | - | Export dialog |
| `shopping-list-share` | **Modal** | - | Share dialog |
| `shopping-recipe-integration` | **Modal** | - | Recipe picker |
| `shopping-category-management` | **Form-Full** | 32-shopping-categories.png | Category form |

---

## Meal Plan Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `meal-plan-dashboard` | **Dashboard** | 40-mealplan-dashboard.png | Stats + plan cards |
| `meal-plan-create` | **Form-Card** | 41-mealplan-create.png | Centered create |
| `meal-plan-edit` | **Form-Full** | - | Edit form |
| `meal-plan-form` | **Inline** | - | Plan fields |
| `meal-plan-detail` | **Detail** | - | Plan view |
| `meal-plan-calendar` | **Calendar** | 42-mealplan-calendar.png | Calendar grid |
| `meal-plan-rules` | **Form-Full** | 43-mealplan-rules.png | Rules config |
| `meal-plan-recipe-selection` | **Search** | 44-mealplan-recipe-selection.png | Recipe picker |
| `meal-plan-to-shopping-list` | **Modal** | 45-mealplan-shopping-list.png | Convert dialog |
| `meal-plan-print` | **Detail** | 46-mealplan-print.png | Print view |
| `meal-plan-nutrition` | **Detail** | 47-mealplan-nutrition.png | Nutrition view |

---

## Recipe Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `recipe-search` | **Search** | 51-recipe-search.png | Browse recipes |
| `recipe-edit` | **Form-Full** | 52-recipe-new.png | Multi-section form |
| `recipe-comments` | **Inline** | - | Comments embed |
| `recipe-rating` | **Inline** | - | Star rating |
| `recipe-ratings` | **Inline** | - | Ratings list |
| `recipe-tags` | **Inline** | - | Tag chips |
| `recipe-assets` | **Inline** | - | Image gallery |
| `recipe-timeline-events` | **Inline** | - | Activity feed |
| `recipe-notes` | **Inline** | - | Notes section |
| `recipe-suggestions` | **Inline** | - | AI suggestions |
| `recipe-share-token` | **Modal** | - | Share dialog |
| `recipe-categories` | **Inline** | - | Category chips |
| `recipe-scraping` | **Modal** | - | Import dialog |
| `ingredient-search` | **Search** | 95-ingredient-search.png | Browse ingredients |
| `ingredient-create` | **Form-Card** | 53-recipe-ingredient-new.png | Centered create |
| `ingredient-edit` | **Modal** | - | Wide edit modal |
| `ingredient-form` | **Inline** | - | Ingredient fields |
| `ingredient-details` | **Detail** | - | Ingredient view |

---

## Communication Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `messaging-inbox` | **Master-Detail** | 60-communication-inbox.png | Thread list + messages |
| `message-compose` | **Modal** | 61-communication-compose.png | Compose dialog |
| `message-thread-detail` | **Detail** | - | Thread view |

---

## Admin Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `user-management` | **Dashboard** | 80-admin-users.png | Search + user cards |

---

## Curation Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `curation-queue` | **Master-Detail** | 70-curation-queue.png | Queue + review panel |

---

## Onboarding Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `onboarding-wizard` | **Wizard** | 90-onboarding-wizard.png | Step container |
| `onboarding-workflow` | **Wizard** | - | Flow container |
| `onboarding-invitation-code` | **Wizard** | 91-onboarding-invitation.png | Code step |
| `onboarding-additional-participants` | **Wizard** | 92-onboarding-participants.png | Choice step |
| `onboarding-restriction-scope` | **Wizard** | 93-onboarding-restrictions.png | Selection step |

---

## Plan Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `curated-plans` | **Search** | 94-curated-plans.png | Browse plans |
| `plan-edit` | **Form-Full** | - | Edit form |

---

## Person Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `person-creation` | **Form-Card** | - | Centered create |
| `person-edit` | **Form-Full** | - | Edit form |
| `person-profile-edit` | **Form-Full** | - | Profile section |
| `person-health-edit` | **Form-Full** | - | Health section |

---

## Restriction Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `restriction-edit` | **Form-Full** | - | Edit form |
| `medical-restriction` | **Form-Full** | - | Medical form |
| `personal-preference` | **Form-Full** | - | Preferences form |
| `societal-restriction` | **Form-Full** | - | Societal form |

---

## Nutrient Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `nutrition-label` | **Inline** | - | FDA nutrition label |

---

## Measurement Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `measurement-converter` | **Inline** | - | Unit converter |
| `measurement-form` | **Inline** | - | Measurement fields |
| `measurement-list` | **Inline** | - | Measurements display |

---

## Privacy Module

| Component | Stereotype | Screenshot | Notes |
|-----------|------------|------------|-------|
| `privacy-analytics` | **Dashboard** | - | Privacy stats |

---

## Summary by Stereotype

| Stereotype | Count | Key Components |
|------------|-------|----------------|
| **Landing** | 2 | home, about |
| **Dashboard** | 7 | recipe-author-dashboard, household-dashboard, shopping-dashboard, meal-plan-dashboard, user-management, privacy-analytics |
| **Form-Card** | 12 | All -create components, auth forms, invite/join |
| **Form-Full** | 18 | All -edit components, settings, rules |
| **Detail** | 6 | household-detail, shopping-detail, meal-plan-detail, ingredient-details |
| **Search** | 5 | recipe-search, ingredient-search, curated-plans, meal-plan-recipe-selection |
| **Master-Detail** | 3 | shopping-list, messaging-inbox, curation-queue |
| **Wizard** | 5 | onboarding-* components |
| **Calendar** | 1 | meal-plan-calendar |
| **Modal** | 8 | login-popover, item-editor, export, share, compose, scraping |
| **Inline** | 14 | Embedded: ratings, tags, comments, nutrition-label |

**Total: ~76 components**

---

## Required Stereotype Files

| File | Status | Purpose |
|------|--------|---------|
| `_stereotype-landing.scss` | **NEW** | Public pages |
| `_stereotype-dashboard.scss` | Updated | Overview pages |
| `_stereotype-form.scss` | Update | Add Form-Card variant |
| `_stereotype-detail.scss` | Exists | Item views |
| `_stereotype-search.scss` | Exists | Browse pages |
| `_stereotype-master-detail.scss` | Exists | Split views |
| `_stereotype-wizard.scss` | Exists | Multi-step flows |
| `_stereotype-calendar.scss` | Exists | Date views |
| `_stereotype-modal.scss` | **NEW** | Dialog overlays |

---

*Last Updated: 2026-01-28*
