# Angular Material Wrap (AMW) Component Guide

This document provides implementation patterns for AMW components used in the NOM application. AMW is a wrapper library around Angular Material that provides standardized, themed components.

## Table of Contents

1. [General Principles](#general-principles)
2. [AmwPopoverComponent](#amwpopovercomponent)
3. [AmwCardComponent](#amwcardcomponent)
4. [AmwButtonComponent](#amwbuttoncomponent)
5. [Theme Integration](#theme-integration)

---

## General Principles

### Always Use AMW Components

When implementing UI features, always prefer AMW components over:
- Custom div-based implementations
- Direct Angular Material components
- Native HTML elements for interactive components

AMW components automatically inherit the application theme and provide consistent styling.

### Import Pattern

```typescript
import {
  AmwButtonComponent,
  AmwIconComponent,
  AmwCardComponent,
  AmwPopoverComponent
} from 'angular-material-wrap';

@Component({
  standalone: true,
  imports: [
    AmwButtonComponent,
    AmwIconComponent,
    AmwCardComponent,
    AmwPopoverComponent
  ],
  // ...
})
```

---

## AmwPopoverComponent

### Overview

`AmwPopoverComponent` provides a popover/tooltip UI with configurable trigger behavior, positioning, and content. It manages its own open/close state.

### Basic Setup

```typescript
import { Component, signal } from '@angular/core';
import { AmwPopoverComponent, AmwButtonComponent } from 'angular-material-wrap';

@Component({
  selector: 'app-my-component',
  standalone: true,
  imports: [AmwPopoverComponent, AmwButtonComponent],
  templateUrl: './my-component.html'
})
export class MyComponent {
  // Optional: track open state with two-way binding
  popoverOpen = signal(false);

  // Trigger configuration - controls how popover opens/closes
  // NOTE: Use 'as const' on type to preserve literal type for TypeScript
  popoverTrigger = {
    type: 'click' as const,  // 'click' | 'hover' | 'focus' | 'manual'
    toggle: true,            // Clicking trigger again closes popover
    escapeKey: true,         // Allow ESC key to close
    outsideClick: true       // Allow clicking outside to close
  };
}
```

### Template Pattern

**IMPORTANT:** Use `ng-template #trigger` and `ng-template #content` for defining the trigger element and popover content.

```html
<amw-popover
  [trigger]="popoverTrigger"
  [(opened)]="popoverOpen"
  position="auto"
  size="medium"
  [showArrow]="true"
  [showClose]="true"
  [showHeader]="false"
  [showFooter]="false"
  closeButtonIcon="close">

  <!-- Trigger Button - what the user clicks to open -->
  <ng-template #trigger>
    <amw-button appearance="elevated" color="primary" icon="info">
      Open Popover
    </amw-button>
  </ng-template>

  <!-- Popover Content - what appears in the popover -->
  <ng-template #content>
    <div class="my-popover-content">
      <p>This is the popover content.</p>
    </div>
  </ng-template>
</amw-popover>
```

### Configuration Options

#### Trigger Configuration

| Property | Type | Description |
|----------|------|-------------|
| `type` | `'click' \| 'hover'` | How the popover opens |
| `toggle` | `boolean` | Whether clicking trigger again closes popover |
| `escapeKey` | `boolean` | Allow ESC key to close (default: true) |
| `outsideClick` | `boolean` | Allow clicking outside to close (default: true) |

**Close-button-only behavior (special cases only):**
```typescript
// Only use this for modals that require explicit dismissal
popoverTrigger = {
  type: 'click' as const,
  toggle: true,
  escapeKey: false,      // Disable ESC closing
  outsideClick: false    // Disable outside-click closing
};
```

#### Popover Properties

| Property | Type | Description |
|----------|------|-------------|
| `position` | `string` | Positioning: `'auto'`, `'top'`, `'bottom'`, `'left'`, `'right'`, `'top-left'`, `'top-right'`, `'bottom-left'`, `'bottom-right'` |
| `size` | `string` | Size preset: `'small'` (200px), `'medium'` (300px), `'large'` (400px), `'extra-large'` (480-520px) |
| `showArrow` | `boolean` | Show directional arrow pointing to trigger |
| `showClose` | `boolean` | Show X button in upper right |
| `showHeader` | `boolean` | Show header section |
| `showFooter` | `boolean` | Show footer section |
| `closeButtonIcon` | `string` | Material icon name for close button (default: `'close'`) |
| `width` | `string` | Custom width (e.g., `'400px'`) |
| `height` | `string` | Custom height (e.g., `'250px'`) |

#### Output Events

```html
<amw-popover
  (beforeOpen)="onBeforeOpen()"
  (afterOpen)="onAfterOpen()"
  (beforeClose)="onBeforeClose()"
  (afterClose)="onAfterClose()"
  (toggle)="onToggle($event)"
  (close)="onClose()">
```

### Auto Positioning Behavior

When `position="auto"`:
- **Horizontal:** If trigger is in left 15% of viewport → popover goes right; right 15% → goes left
- **Vertical:** If trigger is in top half → popover appears below; bottom half → appears above
- **Viewport clamping:** Popover stays within viewport bounds with 12px margin
- **Gap:** 12px spacing between trigger and popover

### Complete Example: Nutrition Label Popover

```typescript
// Component
export class RecipeDetailComponent {
  nutritionPopoverOpen = signal(false);

  nutritionPopoverTrigger = {
    type: 'click' as const,
    toggle: true,
    escapeKey: true,
    outsideClick: true
  };
}
```

```html
<!-- Template -->
<amw-popover
  class="nutrition-popover"
  [trigger]="nutritionPopoverTrigger"
  [(opened)]="nutritionPopoverOpen"
  position="auto"
  size="large"
  [showArrow]="true"
  [showClose]="true"
  [showHeader]="false"
  [showFooter]="false"
  closeButtonIcon="close">

  <ng-template #trigger>
    <button class="nutrition-link">
      Detailed nutrition information
    </button>
  </ng-template>

  <ng-template #content>
    <nom-nutrition-label [data]="nutritionLabelData()"></nom-nutrition-label>
  </ng-template>
</amw-popover>
```

### Simple Text Popover (Alternative Pattern)

For simple text content, you can use the shorthand properties instead of templates:

```html
<amw-popover
  headerTitle="Recipe Info"
  [content]="recipe.description"
  [showHeader]="true"
  [showClose]="true"
  triggerIcon="info_outline"
  triggerText=""
  position="auto">
</amw-popover>
```

**Properties for simple text popovers:**
- `headerTitle` - Title shown in header
- `content` - Text content (string only)
- `triggerIcon` - Material icon name for trigger button
- `triggerText` - Text for trigger button

---

## AmwCardComponent

### Basic Usage

```html
<amw-card
  [headerTitle]="'Card Title'"
  [headerSubtitle]="'Subtitle text'"
  [headerIcon]="'restaurant'"
  [image]="imageUrl"
  [imageAlt]="'Image description'"
  imageHeight="180px"
  [clickable]="true"
  (cardClick)="onCardClick()">

  <ng-template #cardContent>
    <p>Card content goes here</p>
  </ng-template>
</amw-card>
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `headerTitle` | `string` | Card title |
| `headerSubtitle` | `string` | Subtitle below title |
| `headerIcon` | `string` | Material icon name in header |
| `image` | `string` | Image URL |
| `imageAlt` | `string` | Image alt text |
| `imageHeight` | `string` | Image height (e.g., `'180px'`) |
| `clickable` | `boolean` | Makes entire card clickable |

### Content Projection

Use `ng-template #cardContent` for the card body content:

```html
<amw-card headerTitle="Ingredients" headerIcon="list">
  <ng-template #cardContent>
    <ul class="ingredient-list">
      @for (item of ingredients; track item.name) {
        <li>{{ item.quantity }} {{ item.name }}</li>
      }
    </ul>
  </ng-template>
</amw-card>
```

---

## AmwButtonComponent

### Variants

```html
<!-- Filled (primary action) -->
<amw-button variant="filled" color="primary" icon="save">Save</amw-button>

<!-- Outlined (secondary action) -->
<amw-button variant="outlined" icon="cancel">Cancel</amw-button>

<!-- Text (tertiary action) -->
<amw-button variant="text" icon="info">Learn More</amw-button>

<!-- Icon only -->
<amw-button variant="icon" icon="close"></amw-button>

<!-- Elevated -->
<amw-button appearance="elevated" color="primary" icon="add">Add Item</amw-button>
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `variant` | `string` | `'filled'`, `'outlined'`, `'text'`, `'icon'` |
| `appearance` | `string` | `'elevated'`, `'flat'` |
| `color` | `string` | `'primary'`, `'accent'`, `'warn'` |
| `icon` | `string` | Material icon name |
| `iconPosition` | `string` | `'left'`, `'right'` |
| `disabled` | `boolean` | Disable button |

---

## Theme Integration

AMW components automatically use Material Design tokens from the application theme:

| Element | CSS Token |
|---------|-----------|
| Background | `--mat-sys-surface` / `--mdc-theme-surface` |
| Text | `--mat-sys-on-surface` / `--mdc-theme-on-surface` |
| Border | `--mdc-theme-outline-variant` |
| Primary | `--mat-sys-primary` / `--mdc-theme-primary` |

### Global Overrides

AMW component theme overrides are defined in `src/_amw-overrides.scss`. This file is imported by the main styles and provides comprehensive theming for all AMW components.

**Key overrides include:**
- Card theming and icon contrast
- Popover theming, sizing, and positioning
- Button visibility and disabled states
- Input styling and hints
- Toggle/switch visibility in dark mode

**Popover BEM Classes (for custom styling):**

| Class | Element |
|-------|---------|
| `.amw-popover__trigger` | Trigger wrapper |
| `.amw-popover__popover` | Outer popover container |
| `.amw-popover__bubble-content` | Main bubble with border-radius |
| `.amw-popover__arrow` | Directional arrow |
| `.amw-popover__header` | Header section |
| `.amw-popover__content` | Main content area |
| `.amw-popover__close` | Close button |
| `.amw-popover__footer` | Footer section |
| `.amw-popover__panel` | CDK overlay panel |

See `src/_amw-overrides.scss` for the complete implementation.

### Dark Mode

The components automatically adapt when `.mat-theme-dark` or `.dark-theme` class is applied to a parent element.

---

## Common Patterns

### Recipe Card with Info Popover

```html
<amw-card
  [image]="recipe.imageUrl"
  [headerTitle]="recipe.name"
  [headerSubtitle]="'by ' + recipe.authorName"
  [clickable]="true"
  (cardClick)="viewRecipe(recipe)">

  <ng-template #cardContent>
    <div class="card-meta">
      <span class="rating">
        <amw-icon name="star"></amw-icon>
        {{ recipe.rating }}
      </span>

      @if (recipe.description) {
        <amw-popover
          [headerTitle]="recipe.name"
          [content]="recipe.description"
          [showHeader]="true"
          [showClose]="true"
          triggerIcon="info_outline"
          triggerText=""
          position="auto"
          (click)="$event.stopPropagation()">
        </amw-popover>
      }
    </div>
  </ng-template>
</amw-card>
```

### Form Section Card

```html
<amw-card headerTitle="Personal Information" headerIcon="person">
  <ng-template #cardContent>
    <form [formGroup]="profileForm">
      <amw-input label="Name" formControlName="name"></amw-input>
      <amw-input label="Email" formControlName="email"></amw-input>
    </form>
  </ng-template>
</amw-card>
```

---

## Troubleshooting

### Popover Not Opening

1. Ensure you're using `ng-template #trigger` and `ng-template #content`
2. Check that `AmwPopoverComponent` is imported in the component
3. Verify the trigger configuration object is defined

### Theme Not Applied

1. Check `_amw-overrides.scss` for missing overrides
2. Ensure the component is within the themed container
3. Use browser dev tools to inspect which CSS variables are being applied

### Content Not Rendering

1. For cards: Use `ng-template #cardContent`
2. For popovers: Use `ng-template #content`
3. Don't use direct content projection (`<amw-card>content here</amw-card>`)

---

## References

- [AMW API Reference](/node_modules/angular-material-wrap/docs/API-REFERENCE.md)
- [AMW Quick Start](/node_modules/angular-material-wrap/docs/CLAUDE-QUICK-START.md)
- [Application Style Guide](./style-guide.md)
- [AMW Overrides](../src/_amw-overrides.scss)
