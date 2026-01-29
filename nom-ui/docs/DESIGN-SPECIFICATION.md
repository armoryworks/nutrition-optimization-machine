# NOM Design Specification

## Overview

This document defines the styling guidelines derived from the reference design ("Recipe & Pantry Manager") to be implemented across all NOM UI components. The specification establishes global stereotype classes and ensures consistent visual language throughout the application.

---

## 1. Design Philosophy Differences

### Reference Design Principles
| Principle | Reference Design | Current Implementation |
|-----------|------------------|------------------------|
| **Content Strategy** | Content-forward (shows data immediately) | Action-forward (search boxes first) |
| **Navigation** | Sidebar filters + top nav + tabs | Top nav only |
| **Information Density** | High density with visual hierarchy | Sparse with excessive whitespace |
| **Visual Richness** | Cards with images, badges, charts | Plain text, minimal styling |
| **Editing Pattern** | Modal overlays preserve context | Full page navigation |
| **Status Communication** | Color-coded badges (Verified/Unverified) | No visible status workflow |

---

## 2. Color System & Contrast

### 2.1 Background Hierarchy (Dark Theme)

The reference design uses a **layered background system** for depth. This is CRITICAL for visual hierarchy.

```scss
// =============================================================================
// BACKGROUND SURFACE HIERARCHY
// =============================================================================
// The reference uses 5 distinct surface levels for depth perception.
// Each level should have minimum 7% luminance difference for accessibility.

// Level 0: Page background (deepest)
$surface-base: #0f172a;           // HSL: 222, 47%, 11%
// Used for: Body background, beneath everything

// Level 1: Primary containers
$surface-container-low: #1e293b;  // HSL: 217, 33%, 17%
// Used for: Sidebar background, card backgrounds, table backgrounds
// Contrast ratio with base: 1.3:1

// Level 2: Interactive surfaces
$surface-container: #334155;      // HSL: 215, 25%, 27%
// Used for: Hover states, form field backgrounds, selected rows
// Contrast ratio with level 1: 1.4:1

// Level 3: Elevated elements
$surface-container-high: #475569; // HSL: 215, 19%, 35%
// Used for: Tooltips, dropdowns, modal overlays, active states
// Contrast ratio with level 2: 1.3:1

// Level 4: Accent panels (DISTINCTIVE)
$surface-accent: #1a2f4a;         // HSL: 212, 48%, 20%
// Used for: Feature cards, highlighted sections, sidebar active
// This has a BLUE TINT that distinguishes it from neutral surfaces
```

### 2.2 Surface Usage Guidelines

**The key principle: Each layer should be visibly distinct from its parent.**

| UI Element | Surface Level | Guideline |
|------------|---------------|-----------|
| Page background | Level 0 | Deepest, darkest |
| Sidebar | Level 1 | Slightly lighter than page |
| Cards | Level 1 | Same as sidebar, distinct from page |
| Card hover | Level 2 | Visible lift effect |
| Table backgrounds | Level 1 | Container surface |
| Table row hover | Level 2 | Clear hover feedback |
| Form inputs | Level 2 | Inset appearance |
| Form input focus | Level 2 + ring | Primary color focus ring |
| Modal backdrop | 60% opacity black | Dims background |
| Modal content | Level 1 | Same as cards |
| Dropdown menus | Level 3 | Elevated above content |

### 2.3 CSS Custom Properties (Add to :root)

```scss
:root {
  // Surface levels
  --nom-surface-0: #0f172a;
  --nom-surface-1: #1e293b;
  --nom-surface-2: #334155;
  --nom-surface-3: #475569;
  --nom-surface-accent: #1a2f4a;

  // Border colors
  --nom-border-subtle: #334155;
  --nom-border-default: #475569;
  --nom-border-strong: #64748b;

  // Text hierarchy
  --nom-text-primary: #f8fafc;
  --nom-text-secondary: #cbd5e1;
  --nom-text-muted: #94a3b8;
  --nom-text-disabled: #64748b;

  // Interactive states
  --nom-hover-overlay: rgba(255, 255, 255, 0.05);
  --nom-active-overlay: rgba(255, 255, 255, 0.1);
  --nom-focus-ring: rgba(59, 130, 246, 0.5);

  // Status colors (translucent backgrounds)
  --nom-status-verified-bg: rgba(16, 185, 129, 0.15);
  --nom-status-verified-text: #34d399;
  --nom-status-verified-border: rgba(16, 185, 129, 0.3);

  --nom-status-unverified-bg: rgba(245, 158, 11, 0.15);
  --nom-status-unverified-text: #fbbf24;
  --nom-status-unverified-border: rgba(245, 158, 11, 0.3);

  --nom-status-pending-bg: rgba(59, 130, 246, 0.15);
  --nom-status-pending-text: #60a5fa;
  --nom-status-pending-border: rgba(59, 130, 246, 0.3);

  --nom-status-user-created-bg: rgba(139, 92, 246, 0.15);
  --nom-status-user-created-text: #a78bfa;
  --nom-status-user-created-border: rgba(139, 92, 246, 0.3);
}
```

### 2.2 Accent Color Usage

| Element | Color Token | Usage |
|---------|-------------|-------|
| Primary actions | `--mat-sys-primary` (#3b82f6) | Buttons, links, active tabs |
| Secondary info | `--mat-sys-tertiary` (#10b981) | Success states, verified badges |
| Borders/Dividers | `--nom-border-subtle` (#334155) | Card borders, table dividers |
| Text primary | `--mat-sys-on-surface` (#f8fafc) | Headings, primary content |
| Text secondary | `--mat-sys-on-surface-variant` (#94a3b8) | Descriptions, metadata |

### 2.3 Status Badge Colors

```scss
// Status system
$status-verified: (
  background: rgba(16, 185, 129, 0.15),  // Translucent green
  text: #10b981,
  border: #10b981
);

$status-unverified: (
  background: rgba(245, 158, 11, 0.15),  // Translucent amber
  text: #f59e0b,
  border: #f59e0b
);

$status-pending: (
  background: rgba(59, 130, 246, 0.15),  // Translucent blue
  text: #3b82f6,
  border: #3b82f6
);

$status-user-created: (
  background: rgba(139, 92, 246, 0.15),  // Translucent purple
  text: #8b5cf6,
  border: #8b5cf6
);
```

---

## 3. Typography Hierarchy

### 3.1 Type Scale

| Role | Size | Weight | Line Height | Usage |
|------|------|--------|-------------|-------|
| Page Title | 1.75rem (28px) | 500 | 1.2 | Main page headings |
| Section Title | 1.25rem (20px) | 600 | 1.3 | Card headers, section titles |
| Card Title | 1.125rem (18px) | 600 | 1.3 | Recipe names, item titles |
| Body | 0.9375rem (15px) | 400 | 1.5 | Primary content |
| Small/Meta | 0.8125rem (13px) | 400 | 1.4 | Timestamps, metadata |
| Badge | 0.75rem (12px) | 500 | 1 | Status badges, chips |

### 3.2 Font Pairing
- **Headings**: System font stack (Roboto on Material)
- **Body**: Same system font, lighter weight
- **Monospace**: 'Courier New' for codes/technical data

---

## 4. Spacing System

### 4.1 Base Unit
All spacing derives from a **4px base unit**. This MUST be consistent across all components.

### 4.2 Spacing Scale

```scss
// =============================================================================
// SPACING TOKENS - USE THESE EVERYWHERE
// =============================================================================
$space-1: 0.25rem;   // 4px  - Icon gaps, badge padding
$space-2: 0.5rem;    // 8px  - Tight element gaps, chip padding
$space-3: 0.75rem;   // 12px - List item padding, input padding
$space-4: 1rem;      // 16px - Standard gap, form field spacing
$space-5: 1.25rem;   // 20px - Card content padding
$space-6: 1.5rem;    // 24px - Section padding, card padding
$space-8: 2rem;      // 32px - Section margins, large gaps
$space-10: 2.5rem;   // 40px - Page horizontal padding
$space-12: 3rem;     // 48px - Empty state padding
$space-16: 4rem;     // 64px - Hero sections
```

### 4.3 Control Spacing Standards (CRITICAL)

These are the EXACT measurements observed in the reference design:

```scss
// =============================================================================
// CONTROL SPACING - MANDATORY FOR ALL COMPONENTS
// =============================================================================

// Form Controls
$control-height: 44px;              // All interactive elements (touch target)
$control-height-sm: 36px;           // Compact controls
$control-height-lg: 52px;           // Large buttons, hero CTAs

$input-padding-x: 16px;             // Horizontal padding inside inputs
$input-padding-y: 12px;             // Vertical padding inside inputs
$input-border-radius: 8px;          // Standard input corners

// Buttons
$button-padding-x: 24px;            // Horizontal padding
$button-padding-x-sm: 16px;         // Small button horizontal
$button-padding-x-lg: 32px;         // Large button horizontal
$button-gap: 12px;                  // Gap between buttons in a group
$button-border-radius: 8px;         // Standard button corners

// Icon Buttons
$icon-button-size: 40px;            // Standard icon button
$icon-button-size-sm: 32px;         // Small icon button
$icon-button-size-lg: 48px;         // Large icon button

// Chips/Badges
$chip-padding-x: 12px;
$chip-padding-y: 4px;
$chip-gap: 8px;                     // Gap between chips
$chip-border-radius: 16px;          // Pill shape

$badge-padding-x: 12px;
$badge-padding-y: 4px;
$badge-border-radius: 4px;          // Slight rounding

// Form Field Gaps
$field-gap-vertical: 16px;          // Between stacked fields
$field-gap-horizontal: 16px;        // Between side-by-side fields
$field-label-gap: 8px;              // Label to input
$field-hint-gap: 4px;               // Input to hint/error

// Section Spacing
$section-gap: 32px;                 // Between major sections
$section-title-gap: 16px;           // Section title to content

// Card Internal Spacing
$card-padding: 20px;                // Card content padding
$card-header-padding: 16px;         // Card header padding
$card-actions-padding: 12px 16px;   // Card actions area
$card-gap: 16px;                    // Gap between cards in grid

// Table Spacing
$table-cell-padding-x: 16px;
$table-cell-padding-y: 12px;
$table-header-padding-y: 12px;

// Sidebar Spacing
$sidebar-padding: 20px;
$sidebar-item-padding: 8px 12px;
$sidebar-item-gap: 4px;
$sidebar-section-gap: 24px;

// Modal Spacing
$modal-padding: 24px;
$modal-header-padding: 16px 24px;
$modal-content-padding: 24px;
$modal-actions-padding: 16px 24px;
$modal-section-gap: 20px;
```

### 4.4 Component-Specific Spacing (Visual Reference)

```
┌─────────────────────────────────────────────────────────────┐
│ CARD SPACING                                                │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │←16px→ Card Header                               ←16px→│ │
│ │       ↕12px                                            │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │←20px→                                           ←20px→│ │
│ │       ↕                                                │ │
│ │       Card Content                                     │ │
│ │       ↕                                                │ │
│ │←20px→                                           ←20px→│ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │←16px→                        [Button]  [Button] ←16px→│ │
│ │       ↕12px                    ←12px→                  │ │
│ └─────────────────────────────────────────────────────────┘ │
│ ↕16px (gap between cards)                                   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ FORM SPACING                                                │
│                                                              │
│ Section Title                                                │
│ ↕16px                                                        │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Label                                                    │ │
│ │ ↕8px                                                     │ │
│ │ ┌─────────────────────────────────────────────────────┐ │ │
│ │ │←16px→ Input text                            ←16px→│ │ │
│ │ │       ↕12px                                        │ │ │
│ │ └─────────────────────────────────────────────────────┘ │ │
│ │ ↕4px                                                     │ │
│ │ Hint text                                                │ │
│ └─────────────────────────────────────────────────────────┘ │
│ ↕16px                                                        │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Label                                                    │ │
│ │ ...                                                      │ │
│ └─────────────────────────────────────────────────────────┘ │
│ ↕32px (before next section)                                 │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ TABLE SPACING                                               │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │←16px→ Header 1    │ Header 2    │ Header 3       ←16px→│ │
│ │       ↕12px                                            │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │←16px→ Cell data   │ Cell data   │ Cell data      ←16px→│ │
│ │       ↕12px                                            │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │←16px→ Cell data   │ Cell data   │ Cell data      ←16px→│ │
│ │       ↕12px                                            │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 4.5 Responsive Spacing Adjustments

```scss
// Mobile adjustments (< 768px)
@media (max-width: $breakpoint-mobile) {
  // Reduce horizontal padding
  $card-padding: 16px;
  $modal-padding: 16px;
  $sidebar-padding: 16px;

  // Maintain touch targets
  $control-height: 48px;  // INCREASE for mobile

  // Stack buttons full-width
  .nom-actions {
    flex-direction: column;

    button {
      width: 100%;
    }
  }
}
```

---

## 5. Layout Patterns

### 5.0 Layout Positioning Principles (CRITICAL)

The reference design follows these STRICT layout rules:

```scss
// =============================================================================
// LAYOUT POSITIONING RULES
// =============================================================================

// 1. VIEWPORT MANAGEMENT
// ----------------------
// All pages must fit within the viewport without horizontal scroll.
// Content scrolls within its container, not the page.

.nom-page {
  position: fixed;
  top: 64px;         // Below header
  bottom: 48px;      // Above footer
  left: 0;
  right: 0;
  overflow: hidden;  // Page doesn't scroll
}

.nom-page__content {
  height: 100%;
  overflow-y: auto;  // Content scrolls internally
}

// 2. FLEX DISTRIBUTION
// --------------------
// Headers: space-between with fixed left/right, flexible center
// Content: stretch to fill available space
// Actions: align to end

// Header pattern (3-column)
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 24px;

  &__left { flex-shrink: 0; }           // Fixed size
  &__center { flex: 1; }                 // Flexible
  &__right { flex-shrink: 0; }          // Fixed size
}

// 3. GRID POSITIONING
// -------------------
// Cards: auto-fill with minimum width
// Tables: full width
// Forms: max-width with centering

// Card grid - responsive auto-fill
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

// Two-column split (sidebar + main)
.split-layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 0;
}

// Two-column content (70/30 or 60/40)
.content-with-sidebar {
  display: grid;
  grid-template-columns: 1fr 320px;
  gap: 24px;
}

// 4. STICKY POSITIONING
// ---------------------
// Sidebar: sticky to viewport
// Table headers: sticky to container
// Modal actions: sticky to bottom

.sidebar {
  position: sticky;
  top: 64px;
  height: calc(100vh - 112px);
  overflow-y: auto;
}

.table-header {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--nom-surface-1);
}

.modal-actions {
  position: sticky;
  bottom: 0;
  background: var(--nom-surface-1);
  border-top: 1px solid var(--nom-border-subtle);
}

// 5. Z-INDEX HIERARCHY
// --------------------
$z-base: 0;
$z-dropdown: 100;
$z-sticky: 200;
$z-modal-backdrop: 900;
$z-modal: 1000;
$z-toast: 1100;
$z-tooltip: 1200;
```

### 5.1 Page Layout Structure

```
┌─────────────────────────────────────────────────────────────┐
│                        TOP NAV (64px)                        │
├────────────┬────────────────────────────────────────────────┤
│            │                                                 │
│  SIDEBAR   │              MAIN CONTENT                       │
│  (280px)   │                                                 │
│            │  ┌─────────────────────────────────────────┐   │
│  - Filters │  │ PAGE HEADER                             │   │
│  - Quick   │  │ Title + Subtitle | Stats | Actions      │   │
│    actions │  └─────────────────────────────────────────┘   │
│            │                                                 │
│            │  ┌─────────────────────────────────────────┐   │
│            │  │ CONTENT AREA                            │   │
│            │  │ (Cards/Tables/Forms)                    │   │
│            │  └─────────────────────────────────────────┘   │
│            │                                                 │
├────────────┴────────────────────────────────────────────────┤
│                        FOOTER (48px)                         │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 Sidebar Pattern

The reference design uses a **persistent left sidebar** for filtering:

```scss
.nom-layout-with-sidebar {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 0;
  min-height: calc(100vh - 112px); // Header + footer

  @media (max-width: 1024px) {
    grid-template-columns: 1fr;
  }
}

.nom-sidebar {
  background: var(--nom-surface-container-low);
  border-right: 1px solid var(--nom-border-subtle);
  padding: $space-5;
  position: sticky;
  top: 64px;
  height: calc(100vh - 112px);
  overflow-y: auto;
}
```

### 5.3 Content Area Width

```scss
// Max content widths
$content-max-narrow: 800px;   // Forms, single-column
$content-max-medium: 1200px;  // Dashboards, lists
$content-max-wide: 1600px;    // Data tables, grids
$content-max-full: 100%;      // Master-detail layouts
```

---

## 6. Component Stereotypes

### 6.1 Dashboard Stereotype

**Use for**: Landing pages, overview screens, dashboards

**Structure**:
```
┌────────────────────────────────────────────────────┐
│ Quick Filter Sidebar  │  Main Dashboard            │
│ ┌──────────────────┐  │  ┌──────────────────────┐  │
│ │ □ My Creations   │  │  │ Stats Pills          │  │
│ │ □ Community      │  │  └──────────────────────┘  │
│ │ □ Pending        │  │  ┌──────┐ ┌──────┐ ┌────┐ │
│ └──────────────────┘  │  │Card 1│ │Card 2│ │... │ │
│                       │  └──────┘ └──────┘ └────┘ │
│                       │  ┌──────────────────────┐  │
│                       │  │ Data Table           │  │
│                       │  └──────────────────────┘  │
└───────────────────────┴────────────────────────────┘
```

**Key Elements**:
- Sidebar with checkbox filters
- Stats pills in header (icon + count + label)
- Card grid for visual items (recipes with images)
- Data table for list items (ingredients)
- Pagination controls

### 6.2 Detail Stereotype

**Use for**: Recipe view, item detail pages

**Structure**:
```
┌────────────────────────────────────────────────────────────┐
│ Breadcrumb: Home > Recipes > Recipe Name                    │
├─────────────────────────────────────────┬──────────────────┤
│ MAIN CONTENT (2/3)                      │ SIDEBAR (1/3)    │
│ ┌─────────────────────────────────────┐ │ ┌──────────────┐ │
│ │ Title + Sync Toggle                 │ │ │ Community    │ │
│ └─────────────────────────────────────┘ │ │ Updates      │ │
│ ┌─────────────────────────────────────┐ │ │              │ │
│ │ Nutrition Summary (Pie Chart)       │ │ │ - Activity 1 │ │
│ └─────────────────────────────────────┘ │ │ - Activity 2 │ │
│ ┌─────────────────────────────────────┐ │ └──────────────┘ │
│ │ Ingredients Table                   │ │                  │
│ │ Name | Qty | Sync                   │ │                  │
│ └─────────────────────────────────────┘ │                  │
└─────────────────────────────────────────┴──────────────────┘
```

**Key Elements**:
- Breadcrumb navigation
- Content + sidebar layout (70/30 split)
- Inline editing controls (quantity inputs)
- Sync toggles for community features
- Activity feed in sidebar

### 6.3 Form Stereotype

**Use for**: Create/edit screens, settings

**Structure**:
```
┌────────────────────────────────────────────────────┐
│ Form Title                                         │
│ Subtitle/description                               │
├────────────────────────────────────────────────────┤
│ ┌────────────────────────────────────────────────┐ │
│ │ Section: General Information                   │ │
│ │ ┌──────────────────────────────────────────┐   │ │
│ │ │ Label                                    │   │ │
│ │ │ [Input Field                          ]  │   │ │
│ │ └──────────────────────────────────────────┘   │ │
│ │ ┌──────────────────────────────────────────┐   │ │
│ │ │ [Textarea                              ] │   │ │
│ │ └──────────────────────────────────────────┘   │ │
│ └────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────┐ │
│ │ Section: Additional Details                    │ │
│ │ ...                                            │ │
│ └────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────┤
│                              [Cancel] [Save]       │
└────────────────────────────────────────────────────┘
```

### 6.4 Modal Stereotype

**Use for**: Quick edits, confirmations, detail overlays

**Structure**:
```
┌────────────────────────────────────────────────────┐
│ Modal Title                                    [X] │
├────────────────────────────────────────────────────┤
│ ┌────────────────────┬───────────────────────────┐ │
│ │ Form Fields        │ Preview/Info Panel        │ │
│ │ ┌────────────────┐ │ ┌───────────────────────┐ │ │
│ │ │ Field 1        │ │ │ Nutrition Facts       │ │ │
│ │ └────────────────┘ │ │ ┌─────────────────┐   │ │ │
│ │ ┌────────────────┐ │ │ │ Calories: 120   │   │ │ │
│ │ │ Field 2        │ │ │ │ Protein: 5g     │   │ │ │
│ │ └────────────────┘ │ │ └─────────────────┘   │ │ │
│ │                    │ │ ┌─────────────────┐   │ │ │
│ │                    │ │ │ [Recipe Image]  │   │ │ │
│ │                    │ │ └─────────────────┘   │ │ │
│ └────────────────────┴───────────────────────────┘ │
├────────────────────────────────────────────────────┤
│                              [Cancel] [Save]       │
└────────────────────────────────────────────────────┘
```

**Key Elements**:
- Two-column layout for form + preview
- Nutrition label component
- Image preview panel
- Sticky footer with actions

### 6.5 Master-Detail Stereotype

**Use for**: Curation queue, message threads

Already implemented but needs refinement for visual consistency.

---

## 7. Card Patterns

### 7.1 Recipe Card (Visual Card)

```scss
.nom-recipe-card {
  // Structure
  display: flex;
  flex-direction: column;
  border-radius: 12px;
  overflow: hidden;
  background: var(--nom-surface-container-low);
  border: 1px solid var(--nom-border-subtle);
  transition: transform 0.2s, box-shadow 0.2s;

  // Hover state
  &:hover {
    transform: translateY(-4px);
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.3);
  }

  // Image area (top)
  &__image {
    aspect-ratio: 16/10;
    object-fit: cover;
    width: 100%;
  }

  // Content area
  &__content {
    padding: $space-4;
    flex: 1;
  }

  // Title
  &__title {
    font-size: 1.125rem;
    font-weight: 600;
    margin: 0 0 $space-2;
    color: var(--mat-sys-on-surface);
  }

  // Badge (User Created, Community)
  &__badge {
    display: inline-block;
    padding: $space-1 $space-3;
    border-radius: 4px;
    font-size: 0.75rem;
    font-weight: 500;
    background: rgba(139, 92, 246, 0.15);
    color: #a78bfa;
    margin-bottom: $space-3;
  }

  // Actions
  &__actions {
    padding: $space-3 $space-4;
    border-top: 1px solid var(--nom-border-subtle);
  }
}
```

### 7.2 Data Card (Info Card)

```scss
.nom-data-card {
  background: var(--nom-surface-container-low);
  border-radius: 8px;
  border: 1px solid var(--nom-border-subtle);
  padding: $space-5;

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: $space-4;
  }

  &__title {
    font-size: 1rem;
    font-weight: 600;
  }

  &__value {
    font-size: 1.5rem;
    font-weight: 700;
    color: var(--mat-sys-primary);
  }
}
```

---

## 8. Table Patterns

### 8.1 Data Table

```scss
.nom-data-table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;

  thead {
    th {
      padding: $space-3 $space-4;
      text-align: left;
      font-weight: 600;
      font-size: 0.8125rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--mat-sys-on-surface-variant);
      border-bottom: 2px solid var(--nom-border-subtle);
    }
  }

  tbody {
    tr {
      transition: background-color 0.15s;

      &:hover {
        background: var(--mat-sys-surface-container);
      }
    }

    td {
      padding: $space-3 $space-4;
      border-bottom: 1px solid var(--nom-border-subtle);
      vertical-align: middle;
    }
  }

  // Status column
  .status-cell {
    .nom-status-badge {
      // Inline badge styling
    }
  }

  // Action column
  .action-cell {
    text-align: right;

    button {
      opacity: 0.7;

      &:hover {
        opacity: 1;
      }
    }
  }
}
```

### 8.2 Pagination

```scss
.nom-pagination {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: $space-4;
  padding: $space-4 0;
  font-size: 0.875rem;
  color: var(--mat-sys-on-surface-variant);

  &__info {
    // "1-11 of 42"
  }

  &__controls {
    display: flex;
    gap: $space-2;
  }

  &__per-page {
    display: flex;
    align-items: center;
    gap: $space-2;
  }
}
```

---

## 9. Form Controls

### 9.1 Input Fields

```scss
.nom-input {
  width: 100%;
  height: 48px;
  padding: $space-3 $space-4;
  background: var(--mat-sys-surface-container);
  border: 1px solid var(--nom-border-subtle);
  border-radius: 8px;
  color: var(--mat-sys-on-surface);
  font-size: 0.9375rem;
  transition: border-color 0.15s, box-shadow 0.15s;

  &:focus {
    outline: none;
    border-color: var(--mat-sys-primary);
    box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.2);
  }

  &::placeholder {
    color: var(--mat-sys-on-surface-variant);
  }

  // With floating label
  &--labeled {
    padding-top: $space-5;
  }
}

.nom-input-label {
  position: absolute;
  top: $space-3;
  left: $space-4;
  font-size: 0.75rem;
  color: var(--mat-sys-primary);
  font-weight: 500;
}
```

### 9.2 Checkbox Filters (Sidebar)

```scss
.nom-filter-checkbox {
  display: flex;
  align-items: center;
  gap: $space-3;
  padding: $space-2 $space-3;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s;

  &:hover {
    background: var(--mat-sys-surface-container);
  }

  input[type="checkbox"] {
    width: 18px;
    height: 18px;
    accent-color: var(--mat-sys-primary);
  }

  label {
    font-size: 0.875rem;
    color: var(--mat-sys-on-surface);
  }
}
```

### 9.3 Toggle Switch

```scss
.nom-toggle {
  position: relative;
  width: 44px;
  height: 24px;

  input {
    opacity: 0;
    width: 0;
    height: 0;
  }

  .slider {
    position: absolute;
    inset: 0;
    background: var(--mat-sys-surface-container-high);
    border-radius: 12px;
    transition: background-color 0.2s;

    &::before {
      content: '';
      position: absolute;
      width: 20px;
      height: 20px;
      left: 2px;
      top: 2px;
      background: white;
      border-radius: 50%;
      transition: transform 0.2s;
    }
  }

  input:checked + .slider {
    background: var(--mat-sys-primary);

    &::before {
      transform: translateX(20px);
    }
  }
}
```

---

## 10. Status Badges

```scss
.nom-status-badge {
  display: inline-flex;
  align-items: center;
  gap: $space-1;
  padding: $space-1 $space-3;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 500;
  border: 1px solid transparent;

  &--verified {
    background: rgba(16, 185, 129, 0.15);
    color: #34d399;
    border-color: rgba(16, 185, 129, 0.3);
  }

  &--unverified {
    background: rgba(245, 158, 11, 0.15);
    color: #fbbf24;
    border-color: rgba(245, 158, 11, 0.3);
  }

  &--pending {
    background: rgba(59, 130, 246, 0.15);
    color: #60a5fa;
    border-color: rgba(59, 130, 246, 0.3);
  }

  &--user-created {
    background: rgba(139, 92, 246, 0.15);
    color: #a78bfa;
    border-color: rgba(139, 92, 246, 0.3);
  }
}
```

---

## 11. Navigation Components

### 11.1 Tabs

```scss
.nom-tabs {
  display: flex;
  gap: $space-1;
  border-bottom: 1px solid var(--nom-border-subtle);

  &__tab {
    padding: $space-3 $space-5;
    font-size: 0.875rem;
    font-weight: 500;
    color: var(--mat-sys-on-surface-variant);
    background: transparent;
    border: none;
    border-bottom: 2px solid transparent;
    cursor: pointer;
    transition: color 0.15s, border-color 0.15s;

    &:hover {
      color: var(--mat-sys-on-surface);
    }

    &--active {
      color: var(--mat-sys-primary);
      border-bottom-color: var(--mat-sys-primary);
    }
  }
}
```

### 11.2 Breadcrumbs

```scss
.nom-breadcrumbs {
  display: flex;
  align-items: center;
  gap: $space-2;
  font-size: 0.8125rem;
  color: var(--mat-sys-on-surface-variant);
  margin-bottom: $space-4;

  a {
    color: var(--mat-sys-on-surface-variant);
    text-decoration: none;

    &:hover {
      color: var(--mat-sys-primary);
    }
  }

  .separator {
    color: var(--mat-sys-outline);
  }

  .current {
    color: var(--mat-sys-on-surface);
    font-weight: 500;
  }
}
```

---

## 12. Data Visualization

### 12.1 Nutrition Pie Chart

```scss
.nom-nutrition-chart {
  display: flex;
  align-items: center;
  gap: $space-6;

  &__chart {
    width: 120px;
    height: 120px;
    // Use chart library (Chart.js, ngx-charts)
  }

  &__legend {
    display: flex;
    flex-direction: column;
    gap: $space-2;
  }

  &__legend-item {
    display: flex;
    align-items: center;
    gap: $space-2;
    font-size: 0.8125rem;

    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }

    .value {
      font-weight: 600;
      margin-left: auto;
    }
  }
}
```

### 12.2 Nutrition Label

```scss
.nom-nutrition-label {
  background: var(--mat-sys-surface-container);
  border: 2px solid var(--mat-sys-on-surface);
  border-radius: 8px;
  padding: $space-4;
  font-family: 'Arial', sans-serif;
  max-width: 250px;

  &__title {
    font-size: 1.5rem;
    font-weight: 900;
    border-bottom: 8px solid var(--mat-sys-on-surface);
    padding-bottom: $space-2;
    margin-bottom: $space-2;
  }

  &__serving {
    font-size: 0.8125rem;
    border-bottom: 1px solid var(--mat-sys-on-surface);
    padding-bottom: $space-2;
    margin-bottom: $space-2;
  }

  &__calories {
    display: flex;
    justify-content: space-between;
    font-size: 1rem;
    font-weight: 700;
    border-bottom: 4px solid var(--mat-sys-on-surface);
    padding-bottom: $space-2;
    margin-bottom: $space-2;

    .value {
      font-size: 1.5rem;
    }
  }

  &__row {
    display: flex;
    justify-content: space-between;
    font-size: 0.8125rem;
    padding: $space-1 0;
    border-bottom: 1px solid var(--nom-border-subtle);

    &--bold {
      font-weight: 700;
    }

    &--indent {
      padding-left: $space-4;
    }
  }
}
```

---

## 13. Responsive Breakpoints

```scss
$breakpoint-mobile: 768px;
$breakpoint-tablet: 1024px;
$breakpoint-desktop: 1280px;
$breakpoint-wide: 1536px;

// Mobile-first approach
@mixin mobile-only {
  @media (max-width: #{$breakpoint-mobile - 1}) {
    @content;
  }
}

@mixin tablet-up {
  @media (min-width: $breakpoint-mobile) {
    @content;
  }
}

@mixin desktop-up {
  @media (min-width: $breakpoint-tablet) {
    @content;
  }
}

@mixin wide-up {
  @media (min-width: $breakpoint-desktop) {
    @content;
  }
}
```

### Responsive Behaviors

| Element | Mobile (<768px) | Tablet (768-1024) | Desktop (>1024) |
|---------|-----------------|-------------------|-----------------|
| Sidebar | Hidden (hamburger) | Collapsed (icons) | Full width (280px) |
| Card Grid | 1 column | 2 columns | 3-4 columns |
| Page Padding | 16px | 24px | 32px |
| Modal Width | 95% | 600px | 800px |
| Table | Stacked cards | Horizontal scroll | Full table |

---

## 14. Implementation Checklist

### Phase 1: Foundation (Global Styles)
- [ ] Update `_variables.scss` with new color tokens
- [ ] Add status color system
- [ ] Update spacing scale to match spec
- [ ] Add new typography classes

### Phase 2: Layout Patterns
- [ ] Create sidebar layout component
- [ ] Update page container for sidebar support
- [ ] Implement breadcrumb component
- [ ] Update tab styling

### Phase 3: Stereotypes
- [ ] Update `_stereotype-dashboard.scss` with sidebar support
- [ ] Update `_stereotype-detail.scss` with content+sidebar layout
- [ ] Update `_stereotype-form.scss` with modal pattern
- [ ] Create `_stereotype-modal.scss`

### Phase 4: Components
- [ ] Recipe card component
- [ ] Status badge component
- [ ] Data table with pagination
- [ ] Filter checkbox component
- [ ] Nutrition chart component
- [ ] Nutrition label component

### Phase 5: Page Migrations
- [ ] Recipe dashboard (add sidebar, cards)
- [ ] Recipe detail (add breadcrumbs, sidebar)
- [ ] Ingredient edit (modal pattern)
- [ ] Shopping dashboard
- [ ] Meal plan pages

---

## 15. File Structure

```
nom-ui/src/
├── _variables.scss          # Design tokens, colors, spacing
├── _mixins.scss             # Reusable mixins
├── _utilities.scss          # Utility classes
├── _nom-theme.scss          # Material theme configuration
├── _stereotypes.scss        # Index file for stereotypes
├── _stereotype-dashboard.scss
├── _stereotype-detail.scss
├── _stereotype-form.scss
├── _stereotype-modal.scss   # NEW
├── _stereotype-search.scss
├── _stereotype-master-detail.scss
├── _stereotype-wizard.scss
├── _stereotype-calendar.scss
├── _components/             # NEW - Reusable component styles
│   ├── _cards.scss
│   ├── _badges.scss
│   ├── _tables.scss
│   ├── _navigation.scss
│   └── _charts.scss
└── _styles.scss             # Global styles
```

---

---

## 16. Global Stereotype Architecture

### 16.1 Current Problem

Components currently use **inconsistent patterns**:

| Component | Lines of SCSS | Pattern Used | Issue |
|-----------|---------------|--------------|-------|
| `shopping-dashboard` | 41 | Global utility classes | GOOD - minimal |
| `recipe-author-dashboard` | 358 | Redefines `.nom-dashboard` | BAD - duplicates stereotype |
| `meal-plan-dashboard` | 439 | Redefines `.nom-dashboard` | BAD - duplicates stereotype |
| `curation-queue` | 402 | Uses mixins/extends | BETTER - but still verbose |

### 16.2 Target Architecture

**Stereotypes must define GLOBAL CSS classes** that components consume via HTML.

```
┌─────────────────────────────────────────────────────────────┐
│                    GLOBAL STEREOTYPE                         │
│  _stereotype-dashboard.scss                                  │
│  ─────────────────────────────────────────                  │
│  Defines: .nom-dashboard, .nom-dashboard__header,           │
│           .nom-dashboard__grid, .nom-dashboard__card, etc.  │
│                                                              │
│  These classes are available GLOBALLY to all components     │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    COMPONENT HTML                            │
│  recipe-author-dashboard.component.html                      │
│  ─────────────────────────────────────────                  │
│  <div class="nom-dashboard">                                 │
│    <div class="nom-dashboard__header">...</div>             │
│    <div class="nom-dashboard__grid">...</div>               │
│  </div>                                                      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    COMPONENT SCSS (Minimal!)                 │
│  recipe-author-dashboard.component.scss                      │
│  ─────────────────────────────────────────                  │
│  // ONLY positioning overrides specific to THIS component   │
│  .recipe-status-badge--draft { ... }                        │
│  .recipe-image-placeholder { ... }                          │
└─────────────────────────────────────────────────────────────┘
```

### 16.3 What Goes Where

| Location | Contains | Examples |
|----------|----------|----------|
| **_stereotype-*.scss** | All structural, layout, spacing, typography | Grid layouts, card structures, headers, empty states |
| **_styles.scss** | Global utility classes | `.nom-chip`, `.nom-stat-pill`, `.nom-empty-state` |
| **_variables.scss** | Design tokens | Colors, spacing, typography scales |
| **Component .scss** | ONLY component-specific overrides | Unique status colors, domain-specific badges |

### 16.4 Stereotype Class Naming Convention

```scss
// Block: The stereotype container
.nom-dashboard { }
.nom-detail { }
.nom-form { }
.nom-master-detail { }

// Element: Child elements using BEM
.nom-dashboard__header { }
.nom-dashboard__grid { }
.nom-dashboard__card { }
.nom-dashboard__empty-state { }

// Modifier: Variants
.nom-dashboard--compact { }
.nom-dashboard__card--highlighted { }
```

### 16.5 Dashboard Stereotype - Global Classes

The following classes should be defined GLOBALLY in `_stereotype-dashboard.scss`:

```scss
// =============================================================================
// DASHBOARD STEREOTYPE - GLOBAL CLASSES
// =============================================================================
// These classes are consumed directly in component HTML templates.
// Components should NOT redefine these - only extend with component-specific styles.

// -----------------------------------------------------------------------------
// Container
// -----------------------------------------------------------------------------
.nom-dashboard {
  width: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: $spacing-4 $spacing-6;
  min-height: calc(100vh - 120px);

  @media (max-width: $breakpoint-mobile) {
    padding: $spacing-3 $spacing-4;
  }
}

// -----------------------------------------------------------------------------
// Layout with Sidebar
// -----------------------------------------------------------------------------
.nom-dashboard--with-sidebar {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 0;
  padding: 0;

  @media (max-width: $breakpoint-tablet) {
    grid-template-columns: 1fr;
  }
}

.nom-dashboard__sidebar {
  background: var(--nom-surface-container-low);
  border-right: 1px solid var(--nom-border-subtle);
  padding: $spacing-5;
  position: sticky;
  top: 64px;
  height: calc(100vh - 112px);
  overflow-y: auto;

  @media (max-width: $breakpoint-tablet) {
    display: none; // Hide on mobile, use drawer instead
  }
}

.nom-dashboard__main {
  padding: $spacing-4 $spacing-6;
  overflow-y: auto;
}

// -----------------------------------------------------------------------------
// Header
// -----------------------------------------------------------------------------
.nom-dashboard__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: $spacing-6;
  margin-bottom: $spacing-6;
  min-height: 48px;

  @media (max-width: $breakpoint-mobile) {
    flex-direction: column;
    align-items: stretch;
    gap: $spacing-3;
  }
}

.nom-dashboard__header-left {
  display: flex;
  align-items: baseline;
  gap: $spacing-3;
  flex-shrink: 0;
}

.nom-dashboard__header-center {
  display: flex;
  align-items: center;
  gap: $spacing-3;
  flex: 1;
  justify-content: center;

  @media (max-width: $breakpoint-mobile) {
    justify-content: flex-start;
    flex-wrap: wrap;
  }
}

.nom-dashboard__header-right {
  display: flex;
  align-items: center;
  gap: $spacing-3;
  flex-shrink: 0;
}

.nom-dashboard__title {
  font-size: $font-size-xl;
  font-weight: $font-weight-semibold;
  margin: 0;
  color: var(--mat-sys-on-surface);
}

.nom-dashboard__subtitle {
  font-size: $font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

// -----------------------------------------------------------------------------
// Tabs
// -----------------------------------------------------------------------------
.nom-dashboard__tabs {
  display: flex;
  gap: $spacing-1;
  border-bottom: 1px solid var(--nom-border-subtle);
  margin-bottom: $spacing-6;
}

.nom-dashboard__tab {
  padding: $spacing-3 $spacing-5;
  font-size: $font-size-sm;
  font-weight: $font-weight-medium;
  color: var(--mat-sys-on-surface-variant);
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s;

  &:hover {
    color: var(--mat-sys-on-surface);
  }

  &--active {
    color: var(--mat-sys-primary);
    border-bottom-color: var(--mat-sys-primary);
  }
}

// -----------------------------------------------------------------------------
// Content Grid
// -----------------------------------------------------------------------------
.nom-dashboard__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: $spacing-5;

  @media (max-width: $breakpoint-mobile) {
    grid-template-columns: 1fr;
    gap: $spacing-4;
  }
}

.nom-dashboard__grid--2col {
  grid-template-columns: repeat(2, 1fr);

  @media (max-width: $breakpoint-mobile) {
    grid-template-columns: 1fr;
  }
}

// -----------------------------------------------------------------------------
// Cards
// -----------------------------------------------------------------------------
.nom-dashboard__card {
  background: var(--nom-surface-container-low);
  border: 1px solid var(--nom-border-subtle);
  border-radius: $nom-border-radius-lg;
  overflow: hidden;
  transition: transform 0.2s, box-shadow 0.2s;

  &:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  }
}

.nom-dashboard__card-image {
  aspect-ratio: 16/10;
  object-fit: cover;
  width: 100%;
}

.nom-dashboard__card-content {
  padding: $spacing-4;
}

.nom-dashboard__card-title {
  font-size: $font-size-lg;
  font-weight: $font-weight-semibold;
  margin: 0 0 $spacing-2;
  color: var(--mat-sys-on-surface);
}

.nom-dashboard__card-meta {
  font-size: $font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

.nom-dashboard__card-actions {
  padding: $spacing-3 $spacing-4;
  border-top: 1px solid var(--nom-border-subtle);
  display: flex;
  justify-content: flex-end;
  gap: $spacing-2;
}

// -----------------------------------------------------------------------------
// Filters (Sidebar)
// -----------------------------------------------------------------------------
.nom-dashboard__filter-group {
  margin-bottom: $spacing-5;
}

.nom-dashboard__filter-title {
  font-size: $font-size-sm;
  font-weight: $font-weight-semibold;
  color: var(--mat-sys-on-surface);
  margin: 0 0 $spacing-3;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.nom-dashboard__filter-item {
  display: flex;
  align-items: center;
  gap: $spacing-3;
  padding: $spacing-2 $spacing-3;
  border-radius: $nom-border-radius;
  cursor: pointer;
  transition: background-color 0.15s;

  &:hover {
    background: var(--mat-sys-surface-container);
  }

  input[type="checkbox"] {
    width: 18px;
    height: 18px;
    accent-color: var(--mat-sys-primary);
  }

  label {
    font-size: $font-size-sm;
    color: var(--mat-sys-on-surface);
  }
}

// -----------------------------------------------------------------------------
// Data Table
// -----------------------------------------------------------------------------
.nom-dashboard__table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  background: var(--nom-surface-container-low);
  border-radius: $nom-border-radius;
  overflow: hidden;
}

.nom-dashboard__table-header {
  th {
    padding: $spacing-3 $spacing-4;
    text-align: left;
    font-weight: $font-weight-semibold;
    font-size: $font-size-xs;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--mat-sys-on-surface-variant);
    border-bottom: 2px solid var(--nom-border-subtle);
  }
}

.nom-dashboard__table-row {
  transition: background-color 0.15s;

  &:hover {
    background: var(--mat-sys-surface-container);
  }

  td {
    padding: $spacing-3 $spacing-4;
    border-bottom: 1px solid var(--nom-border-subtle);
    vertical-align: middle;
  }

  &:last-child td {
    border-bottom: none;
  }
}

// -----------------------------------------------------------------------------
// Pagination
// -----------------------------------------------------------------------------
.nom-dashboard__pagination {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: $spacing-4;
  padding: $spacing-4 0;
  font-size: $font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

// -----------------------------------------------------------------------------
// Empty State
// -----------------------------------------------------------------------------
.nom-dashboard__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: $spacing-12;
  text-align: center;
}

.nom-dashboard__empty-icon {
  font-size: 4rem;
  color: var(--mat-sys-on-surface-variant);
  opacity: 0.5;
  margin-bottom: $spacing-4;
}

.nom-dashboard__empty-title {
  font-size: $font-size-lg;
  font-weight: $font-weight-semibold;
  margin: 0 0 $spacing-2;
}

.nom-dashboard__empty-message {
  color: var(--mat-sys-on-surface-variant);
  max-width: 400px;
  margin-bottom: $spacing-4;
}
```

### 16.6 HTML Template Pattern

Components should use the global classes directly:

```html
<!-- recipe-author-dashboard.component.html -->
<div class="nom-dashboard nom-dashboard--with-sidebar">
  <!-- Sidebar with filters -->
  <aside class="nom-dashboard__sidebar">
    <div class="nom-dashboard__filter-group">
      <h3 class="nom-dashboard__filter-title">Quick Filter</h3>
      <label class="nom-dashboard__filter-item">
        <input type="checkbox" [(ngModel)]="showMyCreations">
        <span>My Creations</span>
      </label>
      <label class="nom-dashboard__filter-item">
        <input type="checkbox" [(ngModel)]="showCommunity">
        <span>Community Curated</span>
      </label>
    </div>
  </aside>

  <!-- Main content -->
  <main class="nom-dashboard__main">
    <!-- Header -->
    <header class="nom-dashboard__header">
      <div class="nom-dashboard__header-left">
        <h1 class="nom-dashboard__title">Dashboard</h1>
      </div>
      <div class="nom-dashboard__header-center">
        <span class="nom-stat-pill">
          <mat-icon>restaurant</mat-icon>
          <span>{{ recipeCount }} Recipes</span>
        </span>
      </div>
      <div class="nom-dashboard__header-right">
        <button mat-button>New Recipe</button>
      </div>
    </header>

    <!-- Tabs -->
    <nav class="nom-dashboard__tabs">
      <button class="nom-dashboard__tab nom-dashboard__tab--active">My Recipes</button>
      <button class="nom-dashboard__tab">Community Library</button>
    </nav>

    <!-- Cards Grid -->
    <div class="nom-dashboard__grid">
      @for (recipe of recipes; track recipe.id) {
        <article class="nom-dashboard__card">
          <img class="nom-dashboard__card-image" [src]="recipe.image" [alt]="recipe.name">
          <div class="nom-dashboard__card-content">
            <h2 class="nom-dashboard__card-title">{{ recipe.name }}</h2>
            <span class="nom-status-badge nom-status-badge--user-created">User Created</span>
          </div>
          <div class="nom-dashboard__card-actions">
            <button mat-button>View Details</button>
          </div>
        </article>
      } @empty {
        <div class="nom-dashboard__empty">
          <mat-icon class="nom-dashboard__empty-icon">restaurant</mat-icon>
          <h3 class="nom-dashboard__empty-title">No recipes yet</h3>
          <p class="nom-dashboard__empty-message">Create your first recipe to get started.</p>
        </div>
      }
    </div>

    <!-- Data Table -->
    <h2>Ingredients</h2>
    <table class="nom-dashboard__table">
      <thead class="nom-dashboard__table-header">
        <tr>
          <th>Name</th>
          <th>Category</th>
          <th>Status</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        @for (ingredient of ingredients; track ingredient.id) {
          <tr class="nom-dashboard__table-row">
            <td>{{ ingredient.name }}</td>
            <td>{{ ingredient.category }}</td>
            <td><span class="nom-status-badge" [class]="'nom-status-badge--' + ingredient.status">{{ ingredient.status }}</span></td>
            <td><button mat-icon-button><mat-icon>edit</mat-icon></button></td>
          </tr>
        }
      </tbody>
    </table>
    <div class="nom-dashboard__pagination">
      <span>1-11 of 42</span>
      <button mat-icon-button><mat-icon>chevron_left</mat-icon></button>
      <button mat-icon-button><mat-icon>chevron_right</mat-icon></button>
    </div>
  </main>
</div>
```

### 16.7 Component SCSS - What's Allowed

Component SCSS files should be **minimal** - only containing:

```scss
// recipe-author-dashboard.component.scss
// ONLY component-specific overrides - NO structural styles!

// Example: Domain-specific status colors not in global palette
.recipe-status--draft {
  background-color: rgba(158, 158, 158, 0.15);
  color: #9e9e9e;
}

// Example: Component-specific icon positioning
.recipe-thumbnail-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--mat-sys-surface-container);
}
```

**Forbidden in component SCSS:**
- Layout definitions (grid, flex containers)
- Spacing/padding/margin (use global classes)
- Typography (use global heading/text classes)
- Card structures (use `.nom-dashboard__card`)
- Headers (use `.nom-dashboard__header`)

---

## Appendix A: Reference Screenshots Mapping

| Screenshot | Stereotype | Key Patterns |
|------------|------------|--------------|
| Recipe Dashboard | Dashboard | Sidebar, card grid, tabs, table |
| Recipe Detail | Detail | Breadcrumbs, content+sidebar, charts |
| Edit Ingredient | Modal | Two-column modal, nutrition label |
| Meal Plan Dashboard | Dashboard | Stats grid, calendar preview |
| Shopping List | Master-Detail | List + detail panel |
| Curation Queue | Master-Detail | Queue + review panel |
| Onboarding | Wizard | Step indicator, centered content |

---

## Appendix B: Complete Component-to-Stereotype Mapping

### All Application Components

| Component | Current Stereotype | Correct Stereotype | Required Global Classes |
|-----------|-------------------|-------------------|------------------------|
| **Auth** | | | |
| `login` | Form | Form | `.nom-form`, `.nom-form__card` |
| `registration` | Form | Form | `.nom-form`, `.nom-form__card` |
| `forgot-password` | Form | Form | `.nom-form`, `.nom-form__card` |
| `reset-password` | Form | Form | `.nom-form`, `.nom-form__card` |
| `confirm-email` | Form | Form | `.nom-form`, `.nom-form__card` |
| `send-confirmation-email` | Form | Form | `.nom-form`, `.nom-form__card` |
| `update-info` | Form | Form | `.nom-form`, `.nom-form__section` |
| `update-two-factor` | Form | Form | `.nom-form`, `.nom-form__section` |
| **User** | | | |
| `recipe-author-dashboard` | Dashboard | Dashboard | `.nom-dashboard--with-sidebar`, `.nom-dashboard__card`, `.nom-dashboard__table` |
| `privacy-settings` | Form | Form | `.nom-form`, `.nom-form__section` |
| **Household** | | | |
| `household-dashboard` | Dashboard | Dashboard | `.nom-dashboard`, `.nom-dashboard__grid`, `.nom-dashboard__card` |
| `household-create` | Form | Form | `.nom-form`, `.nom-form__section` |
| `household-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `household-detail` | Detail | Detail | `.nom-detail`, `.nom-detail__sidebar` |
| `household-invite` | Form | Form | `.nom-form`, `.nom-form__card` |
| `household-join` | Form | Form | `.nom-form`, `.nom-form__card` |
| `household-settings` | Form | Form | `.nom-form`, `.nom-form__section` |
| **Shopping** | | | |
| `shopping-dashboard` | Dashboard | Dashboard | `.nom-dashboard`, `.nom-dashboard__grid`, `.nom-dashboard__card` |
| `shopping-list` | Master-Detail | Master-Detail | `.nom-master-detail`, `.nom-master-panel`, `.nom-detail-panel` |
| `shopping-create` | Form | Form | `.nom-form`, `.nom-form__section` |
| `shopping-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `shopping-detail` | Detail | Detail | `.nom-detail`, `.nom-detail__content` |
| `shopping-category-management` | Form | Form | `.nom-form`, `.nom-form__section` |
| `shopping-item-editor` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| `shopping-item-form` | Form | Form | `.nom-form`, `.nom-form__fields` |
| `shopping-bulk-editor` | Form | Form | `.nom-form`, `.nom-form__table` |
| `shopping-list-export` | Modal | Modal | `.nom-modal`, `.nom-modal__actions` |
| `shopping-list-share` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| `shopping-recipe-integration` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| **Meal Plan** | | | |
| `meal-plan-dashboard` | Dashboard | Dashboard+Calendar | `.nom-dashboard`, `.nom-calendar__grid` |
| `meal-plan-create` | Form | Form | `.nom-form`, `.nom-form__section` |
| `meal-plan-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `meal-plan-detail` | Detail | Detail | `.nom-detail`, `.nom-detail__sidebar` |
| `meal-plan-calendar` | Calendar | Calendar | `.nom-calendar`, `.nom-calendar__week`, `.nom-calendar__day` |
| `meal-plan-rules` | Form | Form | `.nom-form`, `.nom-form__section` |
| `meal-plan-recipe-selection` | Search | Search | `.nom-search`, `.nom-search__filters`, `.nom-search__results` |
| `meal-plan-to-shopping-list` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| `meal-plan-print` | Detail | Detail (Print) | `.nom-print`, `.nom-print__page` |
| `meal-plan-nutrition` | Detail | Detail | `.nom-detail`, `.nom-detail__charts` |
| `meal-plan-form` | Form | Form | `.nom-form`, `.nom-form__section` |
| **Recipe** | | | |
| `recipe-dashboard` | Dashboard | Dashboard | `.nom-dashboard--with-sidebar`, `.nom-dashboard__card`, `.nom-dashboard__table` |
| `recipe-search` | Search | Search | `.nom-search`, `.nom-search__filters`, `.nom-search__grid` |
| `recipe-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `recipe-form` | Form | Form | `.nom-form`, `.nom-form__section` |
| `recipe-comments` | Detail | Detail (Nested) | `.nom-detail__comments` |
| `recipe-rating` | Component | Inline | `.nom-rating` |
| `recipe-tags` | Component | Inline | `.nom-tags` |
| `recipe-assets` | Component | Grid | `.nom-asset-grid` |
| `recipe-timeline-events` | Component | Timeline | `.nom-timeline` |
| `recipe-notes` | Component | Inline | `.nom-notes` |
| `recipe-suggestions` | Component | Cards | `.nom-suggestion-cards` |
| `recipe-share-token` | Modal | Modal | `.nom-modal`, `.nom-modal__share` |
| `recipe-categories` | Form | Form | `.nom-form`, `.nom-form__chips` |
| `recipe-scraping` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| `ingredient-search` | Search | Search | `.nom-search`, `.nom-search__filters` |
| `ingredient-create` | Form | Form | `.nom-form`, `.nom-form__section` |
| `ingredient-edit` | Modal | Modal (Two-Column) | `.nom-modal--wide`, `.nom-modal__preview` |
| `ingredient-form` | Form | Form | `.nom-form`, `.nom-form__section` |
| `ingredient-details` | Detail | Detail | `.nom-detail`, `.nom-detail__content` |
| **Communication** | | | |
| `messaging-inbox` | Master-Detail | Master-Detail | `.nom-master-detail`, `.nom-master-panel`, `.nom-detail-panel` |
| `message-compose` | Modal | Modal | `.nom-modal`, `.nom-modal__form` |
| `message-thread-detail` | Detail | Detail | `.nom-detail`, `.nom-detail__messages` |
| **Admin** | | | |
| `curation-queue` | Master-Detail | Master-Detail | `.nom-master-detail`, `.nom-master-panel`, `.nom-detail-panel` |
| `user-management` | Dashboard | Dashboard+Table | `.nom-dashboard`, `.nom-dashboard__table` |
| **Onboarding** | | | |
| `onboarding-wizard` | Wizard | Wizard | `.nom-wizard`, `.nom-wizard__steps`, `.nom-wizard__content` |
| `onboarding-workflow` | Wizard | Wizard | `.nom-wizard`, `.nom-wizard__step` |
| `onboarding-invitation-code` | Wizard Step | Wizard | `.nom-wizard__step`, `.nom-wizard__form` |
| `onboarding-additional-participants` | Wizard Step | Wizard | `.nom-wizard__step`, `.nom-wizard__form` |
| `onboarding-restriction-scope` | Wizard Step | Wizard | `.nom-wizard__step`, `.nom-wizard__form` |
| **Plan** | | | |
| `curated-plans` | Search | Search | `.nom-search`, `.nom-search__cards` |
| `plan-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| **Person** | | | |
| `person-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `person-profile-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `person-health-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `person-creation` | Form | Form | `.nom-form`, `.nom-form__card` |
| **Restriction** | | | |
| `restriction-edit` | Form | Form | `.nom-form`, `.nom-form__section` |
| `medical-restriction` | Form | Form | `.nom-form`, `.nom-form__chips` |
| `personal-preference` | Form | Form | `.nom-form`, `.nom-form__chips` |
| `societal-restriction` | Form | Form | `.nom-form`, `.nom-form__chips` |
| **Nutrient** | | | |
| `nutrition-label` | Component | Inline | `.nom-nutrition-label` |
| **Measurement** | | | |
| `measurement-converter` | Component | Inline | `.nom-converter` |
| **Privacy** | | | |
| `privacy-analytics` | Dashboard | Dashboard | `.nom-dashboard`, `.nom-dashboard__charts` |
| **Home** | | | |
| `home` | Landing | Landing | `.nom-landing`, `.nom-landing__hero`, `.nom-landing__features` |

---

## Appendix C: All Stereotype Global Classes

### C.1 Form Stereotype (`_stereotype-form.scss`)

```scss
// Container
.nom-form { }
.nom-form--centered { }  // Card-centered form (login, register)
.nom-form--full { }      // Full-width form (edit pages)

// Card (for centered forms)
.nom-form__card { }
.nom-form__card-header { }
.nom-form__card-title { }
.nom-form__card-subtitle { }
.nom-form__card-content { }
.nom-form__card-footer { }

// Sections
.nom-form__section { }
.nom-form__section-title { }
.nom-form__section-description { }

// Fields
.nom-form__fields { }
.nom-form__field { }
.nom-form__field--half { }
.nom-form__field--third { }
.nom-form__field-hint { }
.nom-form__field-error { }

// Actions
.nom-form__actions { }
.nom-form__actions--sticky { }

// Chips/Tags
.nom-form__chips { }
.nom-form__chip { }
```

### C.2 Detail Stereotype (`_stereotype-detail.scss`)

```scss
// Container
.nom-detail { }
.nom-detail--with-sidebar { }

// Breadcrumbs
.nom-detail__breadcrumbs { }
.nom-detail__breadcrumb { }
.nom-detail__breadcrumb-separator { }

// Header
.nom-detail__header { }
.nom-detail__title { }
.nom-detail__subtitle { }
.nom-detail__actions { }

// Content Layout
.nom-detail__content { }
.nom-detail__main { }
.nom-detail__sidebar { }

// Sections
.nom-detail__section { }
.nom-detail__section-title { }
.nom-detail__section-content { }

// Data Display
.nom-detail__row { }
.nom-detail__label { }
.nom-detail__value { }

// Charts
.nom-detail__charts { }
.nom-detail__chart { }
.nom-detail__chart-legend { }

// Activity Feed
.nom-detail__activity { }
.nom-detail__activity-item { }
.nom-detail__activity-time { }
```

### C.3 Search Stereotype (`_stereotype-search.scss`)

```scss
// Container
.nom-search { }

// Header
.nom-search__header { }
.nom-search__title { }

// Filters
.nom-search__filters { }
.nom-search__filter-row { }
.nom-search__filter-field { }
.nom-search__filter-actions { }

// Results
.nom-search__results { }
.nom-search__results-header { }
.nom-search__results-count { }
.nom-search__results-sort { }

// Grid/List
.nom-search__grid { }
.nom-search__list { }
.nom-search__item { }

// Pagination
.nom-search__pagination { }

// Empty/Loading
.nom-search__empty { }
.nom-search__loading { }
```

### C.4 Master-Detail Stereotype (`_stereotype-master-detail.scss`)

```scss
// Container
.nom-master-detail { }

// Header
.nom-master-detail__header { }
.nom-master-detail__title { }
.nom-master-detail__stats { }

// Content
.nom-master-detail__content { }

// Master Panel
.nom-master-panel { }
.nom-master-panel__header { }
.nom-master-panel__search { }
.nom-master-panel__list { }
.nom-master-panel__item { }
.nom-master-panel__item--selected { }
.nom-master-panel__item-title { }
.nom-master-panel__item-meta { }

// Detail Panel
.nom-detail-panel { }
.nom-detail-panel__header { }
.nom-detail-panel__navigation { }
.nom-detail-panel__content { }
.nom-detail-panel__section { }
.nom-detail-panel__actions { }

// Empty States
.nom-master-detail__empty { }
.nom-master-detail__no-selection { }
```

### C.5 Wizard Stereotype (`_stereotype-wizard.scss`)

```scss
// Container
.nom-wizard { }

// Progress
.nom-wizard__progress { }
.nom-wizard__step-indicator { }
.nom-wizard__step-indicator--active { }
.nom-wizard__step-indicator--completed { }
.nom-wizard__step-line { }

// Content
.nom-wizard__content { }
.nom-wizard__step { }
.nom-wizard__step-title { }
.nom-wizard__step-description { }
.nom-wizard__form { }

// Navigation
.nom-wizard__navigation { }
.nom-wizard__prev { }
.nom-wizard__next { }
.nom-wizard__skip { }
```

### C.6 Calendar Stereotype (`_stereotype-calendar.scss`)

```scss
// Container
.nom-calendar { }

// Header
.nom-calendar__header { }
.nom-calendar__title { }
.nom-calendar__nav { }
.nom-calendar__view-toggle { }

// Week View
.nom-calendar__week { }
.nom-calendar__week-header { }
.nom-calendar__day-header { }
.nom-calendar__day { }
.nom-calendar__day--today { }
.nom-calendar__day-content { }

// Month View
.nom-calendar__month { }
.nom-calendar__month-grid { }
.nom-calendar__date-cell { }

// Events
.nom-calendar__event { }
.nom-calendar__event--meal { }
.nom-calendar__event-title { }
.nom-calendar__event-time { }
```

### C.7 Modal Stereotype (`_stereotype-modal.scss`)

```scss
// Container
.nom-modal { }
.nom-modal--wide { }
.nom-modal--narrow { }

// Header
.nom-modal__header { }
.nom-modal__title { }
.nom-modal__close { }

// Content
.nom-modal__content { }
.nom-modal__content--two-column { }
.nom-modal__form { }
.nom-modal__preview { }

// Actions
.nom-modal__actions { }
.nom-modal__actions--sticky { }

// Specialized
.nom-modal__share { }
.nom-modal__confirm { }
```

### C.8 Landing Stereotype (New - for public pages)

```scss
// Container
.nom-landing { }

// Hero
.nom-landing__hero { }
.nom-landing__hero-title { }
.nom-landing__hero-subtitle { }
.nom-landing__hero-cta { }

// Features
.nom-landing__features { }
.nom-landing__feature { }
.nom-landing__feature-icon { }
.nom-landing__feature-title { }
.nom-landing__feature-description { }

// Sections
.nom-landing__section { }
.nom-landing__section-title { }
```

---

## Appendix D: Migration Checklist

### Phase 1: Update Stereotype Files
- [ ] `_stereotype-dashboard.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-detail.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-form.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-search.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-master-detail.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-wizard.scss` - Convert all placeholders to global classes
- [ ] `_stereotype-calendar.scss` - Convert all placeholders to global classes
- [ ] Create `_stereotype-modal.scss` with global classes
- [ ] Create `_stereotype-landing.scss` with global classes

### Phase 2: Update Components (by module)
Each component needs:
1. HTML updated to use global stereotype classes
2. Component SCSS stripped to only positioning overrides

**Auth Module:**
- [ ] login
- [ ] registration
- [ ] forgot-password
- [ ] reset-password
- [ ] confirm-email
- [ ] send-confirmation-email
- [ ] update-info
- [ ] update-two-factor
- [ ] login-popover

**User Module:**
- [ ] recipe-author-dashboard
- [ ] privacy-settings

**Household Module:**
- [ ] household-dashboard
- [ ] household-create
- [ ] household-edit
- [ ] household-detail
- [ ] household-invite
- [ ] household-join
- [ ] household-settings
- [ ] household-invite-refactored

**Shopping Module:**
- [ ] shopping-dashboard
- [ ] shopping-list
- [ ] shopping-create
- [ ] shopping-edit
- [ ] shopping-detail
- [ ] shopping-category-management
- [ ] shopping-item-editor
- [ ] shopping-item-form
- [ ] shopping-bulk-editor
- [ ] shopping-list-export
- [ ] shopping-list-share
- [ ] shopping-recipe-integration

**Meal Plan Module:**
- [ ] meal-plan-dashboard
- [ ] meal-plan-create
- [ ] meal-plan-edit
- [ ] meal-plan-detail
- [ ] meal-plan-calendar
- [ ] meal-plan-rules
- [ ] meal-plan-recipe-selection
- [ ] meal-plan-to-shopping-list
- [ ] meal-plan-print
- [ ] meal-plan-nutrition
- [ ] meal-plan-form

**Recipe Module:**
- [ ] recipe-search
- [ ] recipe-edit
- [ ] recipe-form
- [ ] recipe-comments
- [ ] recipe-rating
- [ ] recipe-tags
- [ ] recipe-assets
- [ ] recipe-timeline-events
- [ ] recipe-notes
- [ ] recipe-suggestions
- [ ] recipe-share-token
- [ ] recipe-categories
- [ ] recipe-scraping
- [ ] ingredient-search
- [ ] ingredient-create
- [ ] ingredient-edit
- [ ] ingredient-form
- [ ] ingredient-details

**Communication Module:**
- [ ] messaging-inbox
- [ ] message-compose
- [ ] message-thread-detail

**Admin Module:**
- [ ] curation-queue
- [ ] user-management

**Onboarding Module:**
- [ ] onboarding-wizard
- [ ] onboarding-workflow
- [ ] onboarding-invitation-code
- [ ] onboarding-additional-participants
- [ ] onboarding-restriction-scope

**Plan Module:**
- [ ] curated-plans
- [ ] plan-edit

**Person Module:**
- [ ] person-edit
- [ ] person-profile-edit
- [ ] person-health-edit
- [ ] person-creation

**Restriction Module:**
- [ ] restriction-edit
- [ ] medical-restriction
- [ ] personal-preference
- [ ] societal-restriction

**Other Modules:**
- [ ] nutrition-label
- [ ] measurement-converter
- [ ] privacy-analytics
- [ ] home
- [ ] reference-selector

---

---

## Appendix E: Ready-to-Implement SCSS

### E.1 Updated `_variables.scss` Additions

Add these to your existing variables file:

```scss
// =============================================================================
// SURFACE HIERARCHY (Add to _variables.scss)
// =============================================================================
$nom-surface-0: var(--mat-sys-background);
$nom-surface-1: var(--mat-sys-surface-container-low);
$nom-surface-2: var(--mat-sys-surface-container);
$nom-surface-3: var(--mat-sys-surface-container-high);

// =============================================================================
// CONTROL DIMENSIONS (Add to _variables.scss)
// =============================================================================
$control-height: 44px;
$control-height-sm: 36px;
$control-height-lg: 52px;

$input-padding-x: 16px;
$input-padding-y: 12px;
$input-border-radius: 8px;

$button-padding-x: 24px;
$button-padding-x-sm: 16px;
$button-gap: 12px;
$button-border-radius: 8px;

$chip-padding-x: 12px;
$chip-padding-y: 4px;
$chip-gap: 8px;
$chip-border-radius: 16px;

$badge-padding-x: 12px;
$badge-padding-y: 4px;
$badge-border-radius: 4px;

// =============================================================================
// LAYOUT DIMENSIONS (Add to _variables.scss)
// =============================================================================
$sidebar-width: 280px;
$sidebar-width-collapsed: 64px;
$header-height: 64px;
$footer-height: 48px;

$card-min-width: 320px;
$card-gap: 16px;
$card-padding: 20px;
$card-border-radius: 12px;

$modal-width-sm: 400px;
$modal-width-md: 600px;
$modal-width-lg: 800px;
$modal-padding: 24px;

// =============================================================================
// Z-INDEX SCALE (Add to _variables.scss)
// =============================================================================
$z-base: 0;
$z-dropdown: 100;
$z-sticky: 200;
$z-modal-backdrop: 900;
$z-modal: 1000;
$z-toast: 1100;
$z-tooltip: 1200;
```

### E.2 Status Badges (Add to `_styles.scss`)

```scss
// =============================================================================
// STATUS BADGES - Global classes
// =============================================================================
.nom-status-badge {
  display: inline-flex;
  align-items: center;
  gap: vars.$spacing-1;
  padding: vars.$badge-padding-y vars.$badge-padding-x;
  border-radius: vars.$badge-border-radius;
  font-size: vars.$font-size-xs;
  font-weight: vars.$font-weight-medium;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  border: 1px solid transparent;

  &--verified {
    background: var(--nom-status-verified-bg, rgba(16, 185, 129, 0.15));
    color: var(--nom-status-verified-text, #34d399);
    border-color: var(--nom-status-verified-border, rgba(16, 185, 129, 0.3));
  }

  &--unverified {
    background: var(--nom-status-unverified-bg, rgba(245, 158, 11, 0.15));
    color: var(--nom-status-unverified-text, #fbbf24);
    border-color: var(--nom-status-unverified-border, rgba(245, 158, 11, 0.3));
  }

  &--pending {
    background: var(--nom-status-pending-bg, rgba(59, 130, 246, 0.15));
    color: var(--nom-status-pending-text, #60a5fa);
    border-color: var(--nom-status-pending-border, rgba(59, 130, 246, 0.3));
  }

  &--user-created,
  &--draft {
    background: var(--nom-status-user-created-bg, rgba(139, 92, 246, 0.15));
    color: var(--nom-status-user-created-text, #a78bfa);
    border-color: var(--nom-status-user-created-border, rgba(139, 92, 246, 0.3));
  }

  &--approved {
    background: var(--nom-status-verified-bg, rgba(16, 185, 129, 0.15));
    color: var(--nom-status-verified-text, #34d399);
    border-color: var(--nom-status-verified-border, rgba(16, 185, 129, 0.3));
  }

  &--rejected {
    background: rgba(239, 68, 68, 0.15);
    color: #f87171;
    border-color: rgba(239, 68, 68, 0.3);
  }
}
```

### E.3 Updated Dashboard Stereotype (Replace `_stereotype-dashboard.scss`)

```scss
// =============================================================================
// DASHBOARD STEREOTYPE - GLOBAL CLASSES
// =============================================================================
// File: _stereotype-dashboard.scss
// These classes are available globally. Components use them via HTML.

@use './variables' as vars;
@use './mixins' as mixins;

// -----------------------------------------------------------------------------
// Container Variants
// -----------------------------------------------------------------------------
.nom-dashboard {
  width: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: vars.$spacing-4 vars.$spacing-6;
  min-height: calc(100vh - vars.$header-height - vars.$footer-height);

  @include mixins.breakpoint(mobile) {
    padding: vars.$spacing-3 vars.$spacing-4;
  }
}

.nom-dashboard--with-sidebar {
  display: grid;
  grid-template-columns: vars.$sidebar-width 1fr;
  gap: 0;
  max-width: none;
  padding: 0;
  height: calc(100vh - vars.$header-height - vars.$footer-height);

  @include mixins.breakpoint(tablet) {
    grid-template-columns: 1fr;
  }
}

.nom-dashboard--full-width {
  max-width: none;
}

// -----------------------------------------------------------------------------
// Sidebar
// -----------------------------------------------------------------------------
.nom-dashboard__sidebar {
  background: var(--mat-sys-surface-container-low);
  border-right: 1px solid var(--nom-border-subtle, var(--mat-sys-outline-variant));
  padding: vars.$spacing-5;
  position: sticky;
  top: vars.$header-height;
  height: calc(100vh - vars.$header-height - vars.$footer-height);
  overflow-y: auto;

  @include mixins.breakpoint(tablet) {
    display: none;
  }
}

.nom-dashboard__main {
  padding: vars.$spacing-4 vars.$spacing-6;
  overflow-y: auto;
  height: 100%;

  @include mixins.breakpoint(mobile) {
    padding: vars.$spacing-3 vars.$spacing-4;
  }
}

// -----------------------------------------------------------------------------
// Filter Groups (Sidebar)
// -----------------------------------------------------------------------------
.nom-dashboard__filter-group {
  margin-bottom: vars.$spacing-6;
}

.nom-dashboard__filter-title {
  font-size: vars.$font-size-xs;
  font-weight: vars.$font-weight-semibold;
  color: var(--mat-sys-on-surface-variant);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 0 0 vars.$spacing-3;
}

.nom-dashboard__filter-item {
  display: flex;
  align-items: center;
  gap: vars.$spacing-3;
  padding: vars.$spacing-2 vars.$spacing-3;
  border-radius: vars.$nom-border-radius;
  cursor: pointer;
  transition: background-color vars.$transition-duration-fast;

  &:hover {
    background: var(--mat-sys-surface-container);
  }

  input[type="checkbox"] {
    width: 18px;
    height: 18px;
    accent-color: var(--mat-sys-primary);
    cursor: pointer;
  }

  span, label {
    font-size: vars.$font-size-sm;
    color: var(--mat-sys-on-surface);
    cursor: pointer;
  }
}

// -----------------------------------------------------------------------------
// Header
// -----------------------------------------------------------------------------
.nom-dashboard__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: vars.$spacing-6;
  margin-bottom: vars.$spacing-6;
  min-height: 48px;

  @include mixins.breakpoint(mobile) {
    flex-direction: column;
    align-items: stretch;
    gap: vars.$spacing-3;
  }
}

.nom-dashboard__header-left {
  display: flex;
  align-items: baseline;
  gap: vars.$spacing-3;
  flex-shrink: 0;
}

.nom-dashboard__header-center {
  display: flex;
  align-items: center;
  gap: vars.$spacing-3;
  flex: 1;
  justify-content: center;

  @include mixins.breakpoint(mobile) {
    justify-content: flex-start;
    flex-wrap: wrap;
  }
}

.nom-dashboard__header-right {
  display: flex;
  align-items: center;
  gap: vars.$spacing-3;
  flex-shrink: 0;

  @include mixins.breakpoint(mobile) {
    width: 100%;
    justify-content: stretch;

    > * {
      flex: 1;
    }
  }
}

.nom-dashboard__title {
  font-size: vars.$font-size-xl;
  font-weight: vars.$font-weight-semibold;
  margin: 0;
  color: var(--mat-sys-on-surface);
  line-height: vars.$line-height-tight;

  @include mixins.breakpoint(mobile) {
    font-size: vars.$font-size-lg;
  }
}

.nom-dashboard__subtitle {
  font-size: vars.$font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

// -----------------------------------------------------------------------------
// Tabs
// -----------------------------------------------------------------------------
.nom-dashboard__tabs {
  display: flex;
  gap: vars.$spacing-1;
  border-bottom: 1px solid var(--mat-sys-outline-variant);
  margin-bottom: vars.$spacing-6;

  @include mixins.breakpoint(mobile) {
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
  }
}

.nom-dashboard__tab {
  padding: vars.$spacing-3 vars.$spacing-5;
  font-size: vars.$font-size-sm;
  font-weight: vars.$font-weight-medium;
  color: var(--mat-sys-on-surface-variant);
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  transition: color vars.$transition-duration-fast, border-color vars.$transition-duration-fast;
  white-space: nowrap;

  &:hover {
    color: var(--mat-sys-on-surface);
  }

  &--active {
    color: var(--mat-sys-primary);
    border-bottom-color: var(--mat-sys-primary);
  }
}

// -----------------------------------------------------------------------------
// Card Grid
// -----------------------------------------------------------------------------
.nom-dashboard__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(vars.$card-min-width, 1fr));
  gap: vars.$card-gap;

  @include mixins.breakpoint(mobile) {
    grid-template-columns: 1fr;
  }
}

.nom-dashboard__grid--2col {
  grid-template-columns: repeat(2, 1fr);

  @include mixins.breakpoint(mobile) {
    grid-template-columns: 1fr;
  }
}

.nom-dashboard__grid--3col {
  grid-template-columns: repeat(3, 1fr);

  @include mixins.breakpoint(tablet) {
    grid-template-columns: repeat(2, 1fr);
  }

  @include mixins.breakpoint(mobile) {
    grid-template-columns: 1fr;
  }
}

// -----------------------------------------------------------------------------
// Cards
// -----------------------------------------------------------------------------
.nom-dashboard__card {
  background: var(--mat-sys-surface-container-low);
  border: 1px solid var(--mat-sys-outline-variant);
  border-radius: vars.$card-border-radius;
  overflow: hidden;
  transition: transform vars.$transition-duration-normal, box-shadow vars.$transition-duration-normal;

  &:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  }

  &--clickable {
    cursor: pointer;
  }
}

.nom-dashboard__card-image {
  width: 100%;
  aspect-ratio: 16 / 10;
  object-fit: cover;
  background: var(--mat-sys-surface-container);
}

.nom-dashboard__card-content {
  padding: vars.$card-padding;
}

.nom-dashboard__card-title {
  font-size: vars.$font-size-lg;
  font-weight: vars.$font-weight-semibold;
  margin: 0 0 vars.$spacing-2;
  color: var(--mat-sys-on-surface);
  line-height: vars.$line-height-tight;
}

.nom-dashboard__card-subtitle {
  font-size: vars.$font-size-sm;
  color: var(--mat-sys-on-surface-variant);
  margin: 0;
}

.nom-dashboard__card-meta {
  display: flex;
  align-items: center;
  gap: vars.$spacing-3;
  margin-top: vars.$spacing-3;
  font-size: vars.$font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

.nom-dashboard__card-actions {
  padding: vars.$spacing-3 vars.$spacing-4;
  border-top: 1px solid var(--mat-sys-outline-variant);
  display: flex;
  justify-content: flex-end;
  gap: vars.$spacing-2;
}

// -----------------------------------------------------------------------------
// Data Table
// -----------------------------------------------------------------------------
.nom-dashboard__table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  background: var(--mat-sys-surface-container-low);
  border-radius: vars.$nom-border-radius;
  overflow: hidden;

  th, td {
    padding: vars.$spacing-3 vars.$spacing-4;
    text-align: left;
    border-bottom: 1px solid var(--mat-sys-outline-variant);
  }

  th {
    font-weight: vars.$font-weight-semibold;
    font-size: vars.$font-size-xs;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--mat-sys-on-surface-variant);
    background: var(--mat-sys-surface-container-low);
    position: sticky;
    top: 0;
    z-index: 1;
  }

  tbody tr {
    transition: background-color vars.$transition-duration-fast;

    &:hover {
      background: var(--mat-sys-surface-container);
    }

    &:last-child td {
      border-bottom: none;
    }
  }

  .action-cell {
    text-align: right;
    white-space: nowrap;
  }
}

// -----------------------------------------------------------------------------
// Pagination
// -----------------------------------------------------------------------------
.nom-dashboard__pagination {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: vars.$spacing-4;
  padding: vars.$spacing-4 0;
  font-size: vars.$font-size-sm;
  color: var(--mat-sys-on-surface-variant);
}

.nom-dashboard__pagination-info {
  white-space: nowrap;
}

.nom-dashboard__pagination-controls {
  display: flex;
  gap: vars.$spacing-1;
}

.nom-dashboard__per-page {
  display: flex;
  align-items: center;
  gap: vars.$spacing-2;
}

// -----------------------------------------------------------------------------
// Empty State
// -----------------------------------------------------------------------------
.nom-dashboard__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: vars.$spacing-12;
  text-align: center;
  grid-column: 1 / -1; // Span all columns
}

.nom-dashboard__empty-icon {
  font-size: 4rem;
  color: var(--mat-sys-on-surface-variant);
  opacity: 0.5;
  margin-bottom: vars.$spacing-4;
}

.nom-dashboard__empty-title {
  font-size: vars.$font-size-lg;
  font-weight: vars.$font-weight-semibold;
  margin: 0 0 vars.$spacing-2;
  color: var(--mat-sys-on-surface);
}

.nom-dashboard__empty-message {
  color: var(--mat-sys-on-surface-variant);
  max-width: 400px;
  margin: 0 0 vars.$spacing-4;
  line-height: vars.$line-height-normal;
}

// -----------------------------------------------------------------------------
// Section (for grouping content areas)
// -----------------------------------------------------------------------------
.nom-dashboard__section {
  margin-bottom: vars.$spacing-8;
}

.nom-dashboard__section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: vars.$spacing-4;
  gap: vars.$spacing-4;

  @include mixins.breakpoint(mobile) {
    flex-direction: column;
    align-items: stretch;
  }
}

.nom-dashboard__section-title {
  font-size: vars.$font-size-lg;
  font-weight: vars.$font-weight-semibold;
  margin: 0;
  color: var(--mat-sys-on-surface);
}
```

### E.4 Example Component Migration

**Before (recipe-author-dashboard.component.scss - 358 lines):**
```scss
// BAD: Redefines everything
.nom-dashboard {
  width: 100%;
  max-width: 1400px;
  // ... 350+ more lines
}
```

**After (recipe-author-dashboard.component.scss - ~20 lines):**
```scss
// GOOD: Only component-specific overrides
@use '../../../../variables' as vars;

// Component-specific status colors
.recipe-status--draft {
  background-color: rgba(158, 158, 158, 0.15);
  color: #9e9e9e;
}

// Component-specific layout tweak (if truly necessary)
:host {
  // Any host-specific styles
}
```

**Component HTML uses global classes:**
```html
<div class="nom-dashboard nom-dashboard--with-sidebar">
  <aside class="nom-dashboard__sidebar">
    <div class="nom-dashboard__filter-group">
      <h3 class="nom-dashboard__filter-title">Quick Filter</h3>
      <!-- filters use global classes -->
    </div>
  </aside>
  <main class="nom-dashboard__main">
    <header class="nom-dashboard__header">
      <!-- header uses global classes -->
    </header>
    <div class="nom-dashboard__grid">
      <!-- cards use global classes -->
    </div>
  </main>
</div>
```

---

*Document Version: 1.0*
*Last Updated: 2026-01-28*
