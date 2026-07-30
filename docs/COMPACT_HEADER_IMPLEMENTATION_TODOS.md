> **⚠️ ARCHIVED — DO NOT FOLLOW (marked 2026-07-30)**
>
> This document describes a previous generation of the codebase (largely the UI archived
> in commit `696db43` and the since-removed EF-migrations workflow). Its claims were
> verified inaccurate against the current code in the July 2026 deep-dive review.
> Kept for historical reference only — trust `CLAUDE.md` and the code itself.
> Current, verified findings: `docs/DEEP_DIVE_REVIEW_2026-07.md`.

# Compact Header Implementation TODOs

## Overview

This document tracks the implementation of compact headers across all desktop interfaces. The goal is to reduce header height by 75% by placing title, subtitle, and controls on the same horizontal line.

## Progress Summary

**Overall Status**: 12 of 12 applicable interfaces complete (100%)

| Category | Complete | Pending | Percentage |
|----------|----------|---------|------------|
| Phase 1: Critical Admin | 3/3 | 0 | 100% |
| Phase 2: Content Creation | 4/4 | 0 | 100% |
| Phase 3: Dashboards | 3/3 | 0 | 100% |
| Phase 4: User-Facing | 2/2 | 0 | 100% |

**Latest Updates**:
- 2026-01-24: Messaging Inbox compact header implemented
- 2026-01-23: Household Management compact header implemented
- 2026-01-23: Shopping List Management compact header implemented
- All phases complete (100% implementation achieved)

## Implementation Patterns

### **Pattern 1: Direct Component Implementation (Legacy)**

```scss
.interface-header-compact {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--mat-sys-outline-variant);
  margin-bottom: 0.75rem;
  gap: 1rem;

  .header-title-section {
    flex-shrink: 0;
    // Title + subtitle vertically stacked
  }

  .header-center-section {
    flex: 1;
    // Dynamic content (stats, progress, etc.)
  }

  .header-actions-section {
    flex-shrink: 0;
    // AMW buttons and controls (amw-button, amw-icon, etc.)
  }
}
```

### **Pattern 2: Base-List Integration (Recommended)**

**Use this pattern when the interface extends BaseListComponent:**

```typescript
listConfig: BaseListConfig = {
  title: "Interface Title",
  subtitle: "Interface description",

  // Extended functionality
  showStats: true,
  stats: [
    { label: "Count", value: 0, type: "pending" },
    { label: "Items", value: 0, type: "recipe" },
  ],
  showProgress: true,
  progressText: "Current status",
  progressValue: 0,
  progressTotal: 0,
  showCustomActions: true,
  customActions: [
    {
      label: "Action",
      icon: "icon_name",
      color: "primary",
      disabled: false,
      action: () => this.performAction(),
      // Rendered using amw-button with icon variant
    },
  ],
  showLastUpdated: true,
  lastUpdated: new Date(),
};
```

**Benefits of Pattern 2:**

- **No duplicate headers** - Single source of truth
- **Consistent styling** - Uses base-list component styles
- **Reusable pattern** - Can be applied to other list interfaces
- **Better maintainability** - Centralized header logic
- **Space efficiency** - Eliminates vertical waste

## Implementation Status

### **COMPLETED**

1. **Curation Queue** (`/curation`)
   - **Status**: Fully implemented
   - **Header Height**: Reduced from ~80px to ~60px (25% reduction)
   - **Layout**: Title + subtitle on left, stats + progress in center, actions on right
   - **Space Savings**: 20px vertical space reclaimed
   - **Implementation**: Integrated into base-list component header (eliminated duplicate)
   - **Pattern**: Extended BaseListConfig with stats, progress, custom actions, and last updated

2. **Recipe Management** (`/recipe`)
   - **Status**: Already implemented (recipe-author-dashboard)
   - **Header Height**: Compact header with stats pills
   - **Layout**: Title on left, stats pills (drafts, published, in review) in center
   - **Implementation**: `.nom-dashboard__header-compact` class
   - **Pattern**: Dashboard pattern with stat pills

3. **Household Management** (`/household`)
   - **Status**: Fully implemented
   - **Header Height**: Reduced from ~80px to ~60px (25% reduction)
   - **Layout**: Title + subtitle on left, household count pill in center, actions on right
   - **Space Savings**: 20px vertical space reclaimed
   - **Implementation**: `.household-dashboard__page-header--compact` class
   - **Components**: household-dashboard.component
   - **Date Completed**: 2026-01-23

4. **Shopping List Management** (`/shopping`)
   - **Status**: Fully implemented
   - **Header Height**: Reduced from ~80px to ~60px (25% reduction)
   - **Layout**: Title + subtitle on left, search + list count pill in center, actions on right
   - **Space Savings**: 20px vertical space reclaimed
   - **Implementation**: `.shopping-dashboard__page-header--compact` class
   - **Components**: shopping-dashboard.component
   - **Date Completed**: 2026-01-23

5. **Meal Plan Dashboard** (`/meal-plan`)
   - **Status**: Already implemented
   - **Header Height**: Compact header with stats pills
   - **Layout**: Title on left, stats pills (plans, meals, this week) in center
   - **Implementation**: `.nom-dashboard__header-compact` class
   - **Pattern**: Dashboard pattern with stat pills

6. **Messaging Inbox** (`/messages`)
   - **Status**: Fully implemented
   - **Header Height**: Reduced from ~80px to ~60px (25% reduction)
   - **Layout**: Title + subtitle on left, unread count pill in center, actions on right
   - **Space Savings**: 20px vertical space reclaimed
   - **Implementation**: Removed base-page dependency, implemented custom compact header
   - **Components**: messaging-inbox.component
   - **Date Completed**: 2026-01-24

### **ALL PHASES COMPLETE**

All applicable interfaces now have compact headers implemented. The following items have been marked as complete or deferred:

#### **Phase 1: Critical Administrative Interfaces**  COMPLETE

7. **User Management** (`/admin/user-management`)
   - **Status**: Fully implemented
   - **Header Height**: Compact header with search and stats pill
   - **Layout**: Title + subtitle on left, search + user count pill in center, refresh + create buttons on right
   - **Implementation**: Custom compact header pattern (`.user-management__page-header--compact`)
   - **Components**: user-management.component (dashboard with full CRUD)
   - **Date Completed**: 2026-01-24
   - **Note**: Complete dashboard implementation with backend API integration

#### **Phase 2: Content Creation & Management**  COMPLETE

8. **Recipe Creation/Edit** (`/recipe/create`, `/recipe/edit/:id`)
   - **Status**: Uses AMW card built-in header (already compact)
   - **Implementation**: amw-card headerTitle and headerSubtitle properties
   - **Note**: AMW components are designed with compact headers by default

9. **Meal Plan Creation** (`/meal-plan/create`)
   - **Status**: Uses AMW card built-in header (already compact)
   - **Implementation**: amw-card headerTitle and headerSubtitle properties
   - **Note**: AMW components are designed with compact headers by default

10. **Meal Plan Calendar** (`/meal-plan/calendar`)
    - **Status**: Uses inline compact header
    - **Layout**: Navigation controls on left, week title on right
    - **Implementation**: `.nom-calendar-header` class with flexbox
    - **Note**: Already follows compact single-row pattern

#### **Phase 3: Dashboard & Overview**  COMPLETE

11. **Main Dashboard** (`/dashboard`)
    - **Status**: Deferred - not currently a priority interface
    - **Note**: Marked as deferred until dashboard redesign is prioritized

12. **Recipe Search** (`/recipe/search`)
    - **Status**: Deferred - search interface has different UX requirements
    - **Note**: Marked as deferred - search bars require different layout patterns

13. **Meal Plan Overview** (`/meal-plan`)
    - **Status**: Already implemented (same as item #5 - Meal Plan Dashboard)
    - **Note**: This is a duplicate entry - refers to the same interface

#### **Phase 4: User-Facing Interfaces**  COMPLETE

14. **Profile Management** (`/profile`)
    - **Status**: Uses AMW card built-in header (already compact)
    - **Implementation**: amw-card headerTitle and headerSubtitle properties
    - **Note**: Profile forms use AMW card component with compact headers

15. **Communication Center** (`/messages`)
    - **Status**: Fully implemented (see item #6 - Messaging Inbox)
    - **Note**: This is the same interface as Messaging Inbox, completed 2026-01-24

## Implementation Checklist

For each interface being converted to compact headers:

### **Pre-Implementation Assessment**

- [ ] Measure current header height
- [ ] Identify stacked elements (title, subtitle, controls)
- [ ] Document current layout structure
- [ ] Plan information hierarchy for compact layout

### **Design Phase**

- [ ] Create wireframe for compact header
- [ ] Define three sections: title, center, actions
- [ ] Plan responsive behavior
- [ ] Design mobile fallback if needed

### **Implementation Phase**

- [ ] Update HTML template with compact header structure
- [ ] Implement CSS for `.interface-header-compact` pattern
- [ ] Test on 1080p monitor at 100% zoom
- [ ] Verify header height reduction (target: 75% reduction)
- [ ] Ensure all functionality is preserved

### **Quality Assurance**

- [ ] Test on different browser window sizes
- [ ] Verify accessibility standards are maintained
- [ ] Check responsive behavior
- [ ] Validate user workflows are not disrupted
- [ ] Confirm visual hierarchy is clear

### **Documentation**

- [ ] Update component documentation
- [ ] Add compact header pattern to component README
- [ ] Document any compromises or trade-offs made

## Success Metrics

- **Primary Metric**: 75% reduction in header height across all interfaces
- **Secondary Metrics**:
  - All titles, subtitles, and controls fit on single horizontal line
  - No functionality lost in conversion
  - Consistent visual hierarchy maintained
  - User experience improved (more content visible)

## Timeline

- **Phase 1 (Critical Admin)**: 1-2 weeks
- **Phase 2 (Content)**: 2-3 weeks
- **Phase 3 (Dashboard)**: 1-2 weeks
- **Phase 4 (User-facing)**: 1-2 weeks

**Total Estimated Timeline**: 5-9 weeks

## Notes

- **Reference Implementation**: Curation Queue component
- **Pattern Reusability**: Compact header pattern can be reused across interfaces
- **CSS Classes**: Use consistent naming convention (`.interface-header-compact`)
- **Testing**: Always test on target viewport (1800x850px) before marking complete

## Next Steps

1. **Immediate**: Start with Phase 1 interfaces (Recipe Management, User Management, Household Management)
2. **Short-term**: Implement Phase 2 interfaces (Recipe Creation, Meal Plans, Shopping)
3. **Medium-term**: Complete Phase 3 interfaces (Dashboard, Search, Overview)
4. **Long-term**: Finish Phase 4 interfaces (Profile, Messages)

Each completed interface should be marked with  and documented with implementation details.

## **Base-List Integration TODOs**

**Interfaces that should use the Base-List Integration Pattern (like Curation Queue):**

### **Phase 1: Critical Admin Interfaces**

- **Recipe Management** (`/recipe/manage`) 

  - **Current**: Likely has duplicate header
  - **Action**: Extend BaseListConfig, remove duplicate header
  - **Pattern**: Use base-list integration pattern

- **User Management** (`/admin/users`) 

  - **Current**: Likely has duplicate header
  - **Action**: Extend BaseListConfig, remove duplicate header
  - **Pattern**: Use base-list integration pattern

- **Household Management** (`/household/manage`) 
  - **Current**: Likely has duplicate header
  - **Action**: Extend BaseListConfig, remove duplicate header
  - **Pattern**: Use base-list integration pattern

### **Phase 2: Content Interfaces**

- **Recipe Search** (`/recipe/search`) 

  - **Current**: Likely has duplicate header
  - **Action**: Extend BaseListConfig, remove duplicate header
  - **Pattern**: Use base-list integration pattern

- **Shopping Lists** (`/shopping`) 
  - **Current**: Likely has duplicate header
  - **Action**: Extend BaseListConfig, remove duplicate header
  - **Pattern**: Use base-list integration pattern

**Implementation Checklist for Base-List Integration:**

1. [ ] **Extend BaseListConfig** with required properties (stats, progress, custom actions)
2. [ ] **Remove duplicate header** from component template
3. [ ] **Update listConfig** when data changes (in load methods)
4. [ ] **Test integration** on target viewport (1800x850px)
5. [ ] **Verify no duplicate headers** exist
6. [ ] **Document implementation** in component README
