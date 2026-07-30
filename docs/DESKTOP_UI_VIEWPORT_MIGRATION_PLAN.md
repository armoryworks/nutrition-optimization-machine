> **⚠️ ARCHIVED — DO NOT FOLLOW (marked 2026-07-30)**
>
> This document describes a previous generation of the codebase (largely the UI archived
> in commit `696db43` and the since-removed EF-migrations workflow). Its claims were
> verified inaccurate against the current code in the July 2026 deep-dive review.
> Kept for historical reference only — trust `CLAUDE.md` and the code itself.
> Current, verified findings: `docs/DEEP_DIVE_REVIEW_2026-07.md`.

# Desktop UI Viewport Migration Plan

## Overview

This document outlines the plan to ensure all desktop-targeted user interfaces fit within the standard 1800x850px viewport at 100% zoom without requiring horizontal or vertical scrolling.

## Target Specification

- **Resolution**: 1080p (1920x1080)
- **Browser**: Maximized window
- **Effective Viewport**: ~1800x850px (accounting for browser UI, taskbar)
- **Zoom Level**: 100%
- **Scroll Requirement**: NO scrolling on primary workflows

## Migration Priority

### **Phase 1: Critical Administrative Interfaces**  HIGH PRIORITY

These interfaces are used frequently by administrators and power users:

1. ** Curation Queue** (`/curation`) - COMPLETED

   - Status: Optimized for 1800x850px viewport
   - Changes: Reduced padding, optimized layout, eliminated scrolling

2. ** Recipe Management** (`/recipes/new`, `/recipes/:id/edit`) - COMPLETED

   - Status: Optimized for 1800x850px viewport
   - Changes: Reduced section spacing (2rem → 1rem), optimized textarea rows (3 → 2), reduced additional actions margin
   - Date Completed: January 24, 2026
   - Result: Form height reduced from 752px to ~652px (fits within 728px available viewport)

3. ** User Management** (`/admin/user-management`) - COMPLETED

   - Status: Optimized for 1800x850px viewport
   - Changes: Compact header pattern implemented, card-based grid layout, responsive design
   - Date Completed: January 24, 2026
   - Note: Component has search, stats, and actions in compact header; no scrolling needed

4. ** Household Management** (`/household/create`, `/household/:id/edit`, `/household/`) - PARTIALLY COMPLETED

   - Status: Create, Edit, and Dashboard optimized for 1800x850px viewport
   - Changes Completed:
     - **Create**: Reduced header/content padding (2rem → 1.25rem), form gaps (1.5rem → 1rem), textarea min-height (80px → 60px) → 420px → 364px
     - **Edit**: Reduced textarea min-height (80px → 60px) → 470px → 415px
     - **Dashboard**: Reduced card padding (20px → 12px), stats gap (24px → 16px), card gaps (24px → 16px) → enables 5+ households
   - Date Completed: January 24, 2026
   - Note: Detail view (`/household/:id`) requires architectural redesign (tab-based layout) to fit 728px viewport - deferred for future work

### **Phase 2: Content Creation Interfaces**  MEDIUM PRIORITY

Interfaces used for creating and editing content:

5. ** Recipe Creation/Edit** (`/recipe/create`, `/recipe/edit/:id`)

   - Current Issues: Long forms with many fields
   - Required Changes: Multi-step wizard or tabbed interface

6. ** Meal Plan Creation** (`/meal-plan/create`)

   - Current Issues: Calendar view may not fit
   - Required Changes: Compact calendar, optimized controls

7. ** Shopping List Management** (`/shopping`)
   - Current Issues: Long lists may require scrolling
   - Required Changes: Virtual scrolling, compact item display

### **Phase 3: Dashboard and Overview Interfaces**  MEDIUM PRIORITY

Main navigation and overview screens:

8. ** Main Dashboard** (`/dashboard`)

   - Current Issues: Multiple widgets may not fit
   - Required Changes: Responsive grid, collapsible widgets

9. ** Recipe Search** (`/recipe/search`)

   - Current Issues: Search results and filters may be too tall
   - Required Changes: Compact result cards, sidebar filters

10. ** Meal Plan Overview** (`/meal-plan`)
    - Current Issues: Calendar + details may overflow
    - Required Changes: Optimized layout, responsive design

### **Phase 4: User-Facing Interfaces**  LOW PRIORITY

End-user interfaces (already somewhat optimized):

11. ** Profile Management** (`/profile`)

    - Current Issues: Forms may be too long
    - Required Changes: Sectioned forms, better spacing

12. ** Communication Center** (`/messages`)
    - Current Issues: Message threads may require scrolling
    - Required Changes: Fixed height with internal scrolling

## Implementation Strategy

### **Standard Layout Pattern**

```scss
// Main container for desktop interfaces
.desktop-interface {
  height: calc(100vh - 140px); // Account for navigation header
  overflow: hidden;
  display: flex;
  flex-direction: column;

  &__header {
    flex-shrink: 0;
    padding: 0.75rem 1rem;
    border-bottom: 1px solid var(--mat-sys-outline-variant);
  }

  &__content {
    flex: 1;
    overflow: hidden;
    display: grid;
    grid-template-columns: minmax(300px, 400px) 1fr;
    gap: 1rem;
    padding: 1rem;
  }

  &__sidebar {
    overflow-y: auto;
    max-height: calc(100vh - 200px);
  }

  &__main {
    overflow-y: auto;
    max-height: calc(100vh - 200px);
  }
}
```

### **Common Optimization Techniques**

#### **1. Compact Spacing**

```scss
// Reduce padding and margins
.compact-layout {
  padding: 0.75rem 1rem; // Instead of 1.5rem 2rem
  gap: 0.75rem; // Instead of 1.5rem
  margin-bottom: 1rem; // Instead of 2rem
}
```

#### **2. Smart Typography**

```scss
// Optimize font sizes for space efficiency
.desktop-typography {
  h1 {
    font-size: 1.5rem;
  } // Instead of 2rem
  h2 {
    font-size: 1.25rem;
  } // Instead of 1.75rem
  h3 {
    font-size: 1.1rem;
  } // Instead of 1.5rem
  p {
    font-size: 0.9rem;
    line-height: 1.4;
  }
}
```

#### **3. Form Optimization**

```scss
// Compact form layouts
.desktop-form {
  .form-field {
    margin-bottom: 1rem; // Instead of 1.5rem
  }

  .form-actions {
    padding: 1rem 0; // Instead of 2rem 0
    gap: 0.75rem; // Tighter button spacing
  }
}
```

#### **4. Table Optimization**

```scss
// Responsive tables that fit viewport
.desktop-table {
  max-height: calc(100vh - 300px);
  overflow-y: auto;

  th,
  td {
    padding: 0.5rem 0.75rem; // Compact cell padding
    font-size: 0.875rem; // Smaller text
  }
}
```

#### **5. Card Layouts**

```scss
// Compact card designs
.desktop-card {
  padding: 0.75rem 1rem; // Reduced padding
  margin-bottom: 1rem; // Tighter stacking

  .card-header {
    padding-bottom: 0.5rem; // Less header spacing
  }

  .card-content {
    max-height: 300px; // Constrain content height
    overflow-y: auto;
  }
}
```

## Migration Checklist

For each interface being migrated:

### **Pre-Migration Assessment**

- [ ] Measure current interface height at 1800x850px viewport
- [ ] Identify scrolling areas (horizontal and vertical)
- [ ] Document current layout structure
- [ ] Identify content that can be compacted or reorganized

### **Design Phase**

- [ ] Create wireframe for optimized layout
- [ ] Define information hierarchy
- [ ] Plan responsive breakpoints
- [ ] Design collapsible/expandable sections if needed

### **Implementation Phase**

- [ ] Update CSS with viewport-constrained heights
- [ ] Implement compact spacing standards
- [ ] Add overflow handling for scrollable areas
- [ ] Test on 1080p monitor at 100% zoom
- [ ] Verify no scrollbars on primary workflows

### **Quality Assurance**

- [ ] Test on different browser window sizes
- [ ] Verify accessibility standards are maintained
- [ ] Check responsive behavior
- [ ] Validate user workflows are not disrupted
- [ ] Performance testing for complex layouts

### **Documentation**

- [ ] Update component documentation
- [ ] Add viewport requirements to component README
- [ ] Document any compromises or trade-offs made

## Timeline

- **Phase 1 (Critical)**: 2-3 weeks
- **Phase 2 (Content)**: 3-4 weeks
- **Phase 3 (Dashboard)**: 2-3 weeks
- **Phase 4 (User-facing)**: 2-3 weeks

**Total Estimated Timeline**: 9-13 weeks

## Success Metrics

- **Primary Metric**: 0% of desktop interfaces require scrolling on 1800x850px viewport
- **Secondary Metrics**:
  - User task completion time maintained or improved
  - No accessibility regressions
  - Consistent visual hierarchy across interfaces
  - Performance metrics maintained

## Risk Mitigation

### **Content Overflow Risk**

- **Risk**: Some content may be too dense to fit without scrolling
- **Mitigation**: Use progressive disclosure, tabs, accordions, or pagination

### **User Experience Risk**

- **Risk**: Compact layouts may feel cramped
- **Mitigation**: Maintain adequate spacing for usability, use visual hierarchy

### **Responsive Design Risk**

- **Risk**: Desktop optimizations may break mobile layouts
- **Mitigation**: Use responsive design principles, test across breakpoints

### **Development Time Risk**

- **Risk**: Migration may take longer than estimated
- **Mitigation**: Start with highest priority interfaces, allow for iteration

## Maintenance

### **Ongoing Requirements**

- All new desktop interfaces must follow viewport standards
- Regular testing on target resolution
- Quarterly review of existing interfaces
- Update standards as needed based on user feedback

### **Testing Protocol**

1. Set browser to 1920x1080 resolution
2. Maximize browser window
3. Set zoom to 100%
4. Navigate through primary user workflows
5. Verify no horizontal or vertical scrolling occurs
6. Test key interactions and forms
7. Validate responsive behavior

## Conclusion

This migration plan ensures all desktop interfaces provide an optimal user experience within the standard viewport constraints while maintaining functionality and accessibility. The phased approach allows for iterative improvement and risk management while delivering immediate value to users.
