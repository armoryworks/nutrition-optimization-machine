# Functional Requirements

> **⚠️ ARCHIVED — DO NOT FOLLOW (marked 2026-08-17)**
>
> Written 2026-02 and never revised. Many requirements marked NOT IMPLEMENTED
> have since shipped (unified `/plan`, shuffle + roulette, meal-plan exclusions,
> recipe substitutions, pantry). Treat the status column as historical only;
> verify against the code before relying on anything here.

## Overview

This document outlines the functional requirements for the Nutritional Optimization Machine (NOM) platform, organized by domain and implementation status. Requirements are categorized as COMPLETE, IMPLEMENTED, PARTIAL, or NOT IMPLEMENTED based on current development status.

## User Onboarding & Profile Completion

### Objective

To efficiently collect essential user data and personal preferences immediately following account registration, establishing a foundational user profile.

### Requirements

| ID       | Requirement                       | Status      | Details                                                                  |
| -------- | --------------------------------- | ----------- | ------------------------------------------------------------------------ |
| FR-1.1   | Multi-Step Onboarding Workflow    |  COMPLETE | Guided, multi-step process with reusable domain-specific edit components |
| FR-1.2   | Core Profile Data Collection      |  COMPLETE | Essential personal profile details collection                            |
| FR-1.3   | Health Attributes Collection      |  COMPLETE | Optional health-related attributes collection                            |
| FR-1.4   | Iterative Restriction Collection  |  COMPLETE | Dietary restrictions collection with major restriction types             |
| FR-1.4.1 | Restriction Scope Selection       |  COMPLETE | Person or plan-level restriction assignment                              |
| FR-1.4.2 | Individual Restriction Assignment |  COMPLETE | Specific person selection for restrictions                               |
| FR-1.5   | Workflow Control Navigation       |  COMPLETE | Next, Back, Skip, Yes, No, Submit controls                               |
| FR-1.6   | UI-Only Workflow Steps            |  COMPLETE | Decision collection without immediate API interaction                    |
| FR-1.7   | Final Onboarding Data Submission  |  COMPLETE | Single operation submission of all collected data                        |
| FR-1.8   | Multi-Participant Onboarding      |  PARTIAL  | Support for multiple participants with individual preferences            |

### Multi-Participant Onboarding Details

| ID       | Requirement                       | Status      | Details                                                  |
| -------- | --------------------------------- | ----------- | -------------------------------------------------------- |
| FR-1.8.1 | Participation Inquiry             |  COMPLETE | Optional UI step asking about additional participants    |
| FR-1.8.2 | Participant Count                 |  COMPLETE | UI step for specifying number of additional participants |
| FR-1.8.3 | Blank Name Slots                  |  COMPLETE | UI presentation of name slots for participants           |
| FR-1.8.4 | Individual Preference Application |  COMPLETE | Optional step for individual preference application      |
| FR-1.8.5 | Iterative Participant Onboarding  |  PARTIAL  | Reuse of restriction collection for each participant     |

## Dietary Restriction Management & Inference

### Objective

To automatically identify and record dietary restrictions based on user responses during onboarding, streamlining the personalization of nutritional plans.

### Requirements

| ID     | Requirement                      | Status      | Details                                                           |
| ------ | -------------------------------- | ----------- | ----------------------------------------------------------------- |
| FR-2.1 | Restriction Data Capture         |  COMPLETE | Detailed dietary restrictions with categorization                 |
| FR-2.2 | Specific Restriction Details     |  COMPLETE | Complex restriction detail collection across categories           |
| FR-2.3 | Person or Plan-Level Association |  COMPLETE | Restrictions associable with specific individuals or entire plans |
| FR-2.4 | Prevention of Duplicates         |  COMPLETE | System prevents duplicate restriction records                     |
| FR-2.5 | Auditability                     |  COMPLETE | All restriction records include audit information                 |

### Specific Restriction Categories

- **Societal/Religious/Ethical**: Predefined lists (Kosher, Vegan), fasting schedules, mandatory inclusions
- **Allergies/Medical**: Predefined allergy lists, specific conditions (Celiac, Diabetes), gastrointestinal conditions, nutrient restrictions
- **Personal Preferences**: Spice levels, disliked ingredients, textures, cooking methods

## Recipe & Ingredient Management

### Objective

To empower Recipe Authors to create, manage, and version their own recipes and custom ingredients.

### Requirements

| ID        | Requirement                     | Status         | Details                                                                  |
| --------- | ------------------------------- | -------------- | ------------------------------------------------------------------------ |
| FR-RM-1.1 | Recipe Creation                 |  IMPLEMENTED | Create recipes with name, description, ingredients, instructions         |
| FR-RM-1.2 | Reuse-First Ingredient Workflow |  IMPLEMENTED | Prioritize searching for and reusing existing ingredients                |
| FR-RM-1.3 | Custom Ingredient Creation      |  IMPLEMENTED | Secondary workflow for creating new ingredients with nutritional data    |
| FR-RM-1.4 | Recipe & Ingredient Dashboard   |  PARTIAL     | Dashboard for viewing created recipes and ingredients by curation status |
| FR-RM-1.5 | Recipe Modification & Deletion  |  IMPLEMENTED | Modify or delete NonCurated or Rejected recipes                          |
| FR-RM-1.6 | Recipe Versioning               |  IMPLEMENTED | Version property with automatic increment on update                      |
| FR-RM-1.7 | Version Pre-population          |  IMPLEMENTED | Form structure with version tracking on recipe updates                    |
| FR-RM-1.8 | Ingredient Duplicate Prevention |  IMPLEMENTED | Real-time duplicate checking in ingredient creation modal                |
| FR-RM-1.9 | Modal-Based Ingredient Creation |  IMPLEMENTED | Modal dialog with pre-populated name and automatic selection             |

## Content Curation Workflow

### Objective

To provide a robust system for Site-Wide Admins to review and manage user-submitted content, ensuring quality and consistency.

### Requirements

| ID        | Requirement                    | Status         | Details                                                    |
| --------- | ------------------------------ | -------------- | ---------------------------------------------------------- |
| FR-CU-2.1 | Submission for Curation        |  IMPLEMENTED | Submit NonCurated recipes and ingredients for curation     |
| FR-CU-2.2 | Curation Queue                 |  COMPLETE    | Admin interface for viewing PendingCuration content        |
| FR-CU-2.3 | Queue Filtering & Sorting      |  COMPLETE    | Sortable by submission date, filterable by author          |
| FR-CU-2.4 | Curation Approval              |  COMPLETE    | Approve submissions with optional private and public notes |
| FR-CU-2.5 | Curation Rejection             |  COMPLETE    | Reject submissions with required explanatory notes         |
| FR-CU-2.6 | Request for Revision           |  COMPLETE    | Request revisions with required feedback notes             |
| FR-CU-2.7 | Curation Dependency Validation |  COMPLETE    | Prevent recipe approval if ingredients not Curated         |

## User Communication System

### Objective

To facilitate communication between users and admins, supporting both contextual feedback and general interaction.

### Requirements

| ID        | Requirement                  | Status     | Details                                                           |
| --------- | ---------------------------- | ---------- | ----------------------------------------------------------------- |
| FR-CO-3.1 | Email Notifications          |  IMPLEMENTED | Configurable SMTP email sender with NoOp fallback                 |
| FR-CO-3.2 | Internal Messaging           |  IMPLEMENTED | Full messaging with 12 API endpoints and thread management        |
| FR-CO-3.3 | Contextual Messaging         |  IMPLEMENTED | Thread context with RecipeId, IngredientId, PlanId support        |
| FR-CO-3.4 | Arbitrary Messaging          |  IMPLEMENTED | Thread creation with arbitrary participant selection               |
| FR-CO-3.5 | User Discovery for Messaging |  IMPLEMENTED | Person search endpoint for user discovery                         |
| FR-CO-3.6 | Inbox                        |  IMPLEMENTED | Thread listing with unread counts, archive, pin, and search       |
| FR-CO-3.7 | Reply-Only Threads           |  IMPLEMENTED | ThreadType support for Normal and CurationFeedback threads        |

## Administrative Functions

### Objective

To provide authorized users with the tools to manage system roles and permissions.

### Requirements

| ID        | Requirement          | Status      | Details                                                                         |
| --------- | -------------------- | ----------- | ------------------------------------------------------------------------------- |
| FR-AD-4.1 | User Role Management |  COMPLETE | Interface for granting/revoking CanManageCuration and CanManageUserRoles claims |
| FR-AD-4.2 | User List            |  COMPLETE | Searchable list of all users in the system                                      |
| FR-AD-4.3 | Initial Admin Setup  |  COMPLETE | Documented SQL script for creating initial super admin user                     |

## Plan Invitation & Participation

### Objective

To enable users to join existing nutritional plans through a secure invitation mechanism.

### Requirements

| ID     | Requirement           | Status      | Details                                                                      |
| ------ | --------------------- | ----------- | ---------------------------------------------------------------------------- |
| FR-3.1 | Invitation Code Input |  COMPLETE | Optional step for invitation code input during onboarding                    |
| FR-3.2 | Plan Linking          |  COMPLETE | Link user's Person record to corresponding Plan using PlanParticipant        |
| FR-3.3 | Role Assignment       |  COMPLETE | Assign default role (e.g., "Plan Member") upon successful plan participation |

## Authentication & Authorization

### Objective

To secure system access and protect sensitive user data and functionalities.

### Requirements

| ID     | Requirement                        | Status      | Details                                                                           |
| ------ | ---------------------------------- | ----------- | --------------------------------------------------------------------------------- |
| FR-4.1 | Authenticated Access               |  COMPLETE | Most API endpoints require valid authentication token                             |
| FR-4.2 | Anonymous Access for Onboarding    |  COMPLETE | Specific public endpoints accessible without authentication                       |
| FR-4.3 | User Registration                  |  COMPLETE | Support for user registration and Person record creation                          |
| FR-4.4 | User Login                         |  COMPLETE | Support for user login with authentication token issuance                         |
| FR-4.5 | Dual Authentication Scheme Support |  COMPLETE | Support for both Identity.BearerScheme and JwtBearerDefaults.AuthenticationScheme |
| FR-4.6 | Claims-Based Authorization         |  COMPLETE | Claims-based authorization with CanManageCuration and CanManageUserRoles policies |

## Data Privacy & Compliance

### Objective

To ensure compliance with applicable data privacy regulations and provide users with control over their personal data.

### Requirements

| ID       | Requirement                | Status      | Details                                                         |
| -------- | -------------------------- | ----------- | --------------------------------------------------------------- |
| FR-4.5.1 | Privacy Consent Management |  COMPLETE | Granular consent collection during registration and onboarding  |
| FR-4.5.2 | Consent Withdrawal         |  COMPLETE | Users can withdraw previously granted consents                  |
| FR-4.5.3 | Data Subject Rights (GDPR) |  COMPLETE | Right of Access, Rectification, Erasure, and Data Portability   |
| FR-4.5.4 | Privacy Dashboard          |  COMPLETE | Dedicated user interface for managing privacy settings          |
| FR-4.5.5 | Data Processing Logging    |  COMPLETE | All access to and modifications of personal data logged         |
| FR-4.5.6 | Privacy Policy Integration |  COMPLETE | Privacy policy prominently displayed during onboarding          |
| FR-4.5.7 | Data Retention Management  |  COMPLETE | Automated data retention policies aligned with privacy policies |

## Household Management

### Objective

To enable users to create and manage household groups for collaborative meal planning and shopping.

### Requirements

| ID        | Requirement           | Status      | Details                                                 |
| --------- | --------------------- | ----------- | ------------------------------------------------------- |
| FR-HM-5.1 | Household Creation    |  COMPLETE | Create household groups with name and description       |
| FR-HM-5.2 | Member Invitation     |  COMPLETE | Invite members via email with secure tokens             |
| FR-HM-5.3 | Member Management     |  COMPLETE | Add, remove, and manage household members               |
| FR-HM-5.4 | Household Preferences |  COMPLETE | Set household-wide dietary preferences and restrictions |
| FR-HM-5.5 | Cookbook Management   |  IMPLEMENTED | Full CRUD with 8 API endpoints and service layer |

## Shopping Lists

### Objective

To provide comprehensive shopping list functionality for households and individuals.

### Requirements

| ID        | Requirement            | Status      | Details                                         |
| --------- | ---------------------- | ----------- | ----------------------------------------------- |
| FR-SL-6.1 | Shopping List Creation |  COMPLETE | Create shopping lists with name and description |
| FR-SL-6.2 | Item Management        |  COMPLETE | Add, edit, and remove shopping list items       |
| FR-SL-6.3 | Item Categorization    |  COMPLETE | Categorize items with labels and organization   |
| FR-SL-6.4 | Household Integration  |  IMPLEMENTED | Smart shopping list with 12 endpoints plus share/unshare |
| FR-SL-6.5 | Recipe Integration     |  COMPLETE | Generate shopping lists from recipes            |
| FR-SL-6.6 | Pantry-Aware Shopping  | IN PROGRESS | Shopping list = needed ingredients minus pantry stock |

## Pantry & Inventory

### Objective

To track household pantry inventory so the shopping list only shows items that need to be purchased, accounting for perishability and consumption through meal completion.

### Requirements

| ID         | Requirement              | Status        | Details                                                              |
| ---------- | ------------------------ | ------------- | -------------------------------------------------------------------- |
| FR-PI-6A.1 | Pantry Item Tracking     | IN PROGRESS   | Track ingredients on hand with quantity, unit, acquisition date       |
| FR-PI-6A.2 | Expiration Management    | IN PROGRESS   | Auto-compute expected expiration via department-based shelf-life defaults |
| FR-PI-6A.3 | Shopping Needs Computation | IN PROGRESS | Upcoming recipe ingredients minus pantry stock = shopping needs      |
| FR-PI-6A.4 | Meal Completion          | IN PROGRESS   | Mark meals as cooked to deduct ingredients from pantry               |
| FR-PI-6A.5 | Perishability Heuristics | IN PROGRESS   | Department-based defaults (Produce 5d, Meat 3d, Grains 180d, etc.)  |

## Meal Planning

### Objective

To enable users to plan meals via a unified plan page with a weekly calendar, inline nutrition display, recipe roulette, member exclusions, and integration with shopping lists and the substitution system.

### Requirements

| ID         | Requirement                          | Status             | Details                                                                       |
| ---------- | ------------------------------------ | ------------------ | ----------------------------------------------------------------------------- |
| FR-MP-7.1  | Meal Plan Creation                   |  COMPLETE        | Create meal plans with name and description                                   |
| FR-MP-7.2  | Meal Entry Management                |  COMPLETE        | Add, edit, and remove meal entries                                            |
| FR-MP-7.3  | Recipe Integration                   |  COMPLETE        | Link recipes to meal plan entries                                             |
| FR-MP-7.4  | Date-Based Planning                  |  COMPLETE        | Plan meals for specific dates and times                                       |
| FR-MP-7.5  | Household Integration                |  COMPLETE        | Share meal plans with household members                                       |
| FR-MP-7.6  | Unified Plan Page                    | NOT IMPLEMENTED    | Single `/plan` page with weekly calendar, sidebar, and inline nutrition       |
| FR-MP-7.7  | Recipe Assignment Dialog             | NOT IMPLEMENTED    | Click empty cell → recipe search dialog with dietary filtering                |
| FR-MP-7.8  | Recipe Roulette                      | NOT IMPLEMENTED    | Random recipe suggestion filtered by dietary restrictions and caloric targets  |
| FR-MP-7.9  | Member Exclusions                    | NOT IMPLEMENTED    | Day-level and per-meal household member exclusions                            |
| FR-MP-7.10 | Inline Nutrition Display             | NOT IMPLEMENTED    | Per-meal calorie/macro chips and per-day summary totals on calendar           |
| FR-MP-7.11 | Meal Slot Notes                      | NOT IMPLEMENTED    | Freeform notes/journal per meal cell                                          |
| FR-MP-7.12 | View Toggles                         | NOT IMPLEMENTED    | Weekly (primary), daily detail, and monthly overview view options             |
| FR-MP-7.13 | Configurable Shopping List Frequency | NOT IMPLEMENTED    | User-configurable generation interval (weekly, bi-weekly, monthly, custom)    |
| FR-MP-7.14 | Plan Context Sidebar                 | NOT IMPLEMENTED    | Sidebar showing plan goals, member restrictions, and weekly exclusion summary |

## Ingredient Substitution System

### Objective

To provide a unified system for managing ingredient substitutions and eliminations across three scopes (curated, personal, recipe-specific), enabling dietary-aware recipe adaptation with configurable restriction severity.

### Requirements

| ID        | Requirement                          | Status          | Details                                                                          |
| --------- | ------------------------------------ | --------------- | -------------------------------------------------------------------------------- |
| FR-IS-8.1 | Curated Substitution Rules           | NOT IMPLEMENTED | Admin-managed communal substitution reference data available to all users         |
| FR-IS-8.2 | Personal Substitution Rules          | NOT IMPLEMENTED | User-scoped substitution overrides that take priority over curated rules          |
| FR-IS-8.3 | Recipe-Specific Overrides            | NOT IMPLEMENTED | Instance-level substitution decisions for specific meal plan entries              |
| FR-IS-8.4 | Unified Substitution/Elimination     | NOT IMPLEMENTED | Single data model: null replacement = elimination, non-null = substitution       |
| FR-IS-8.5 | Restriction Severity Levels          | NOT IMPLEMENTED | 1-5 scale per restriction per person; affects filtering and ranking behavior     |
| FR-IS-8.6 | Substitution Priority Chain          | NOT IMPLEMENTED | Resolution order: recipe-specific > personal > curated                           |
| FR-IS-8.7 | Unresolved Restriction Flagging      | NOT IMPLEMENTED | System flags restricted ingredients with no substitution rule for user decision   |
| FR-IS-8.8 | Substitution Nutrition Recalculation | NOT IMPLEMENTED | Nutrition calculations use replacement ingredient data when substitutions applied |

## Implementation Status Summary

### Backend Status: COMPLETE 

- All database entities implemented
- All API controllers with proper authorization
- All orchestration services with business logic
- All data models for API communication
- All database migrations applied

### Frontend Status: PARTIALLY COMPLETE 

- **Recipe Management**:  IMPLEMENTED - Full CRUD functionality
- **Curation Queue**:  COMPLETE - Admin interface with Material 3 theming
- **Authentication**:  COMPLETE - Full authentication flow
- **Privacy Features**:  COMPLETE - Data subject rights management
- **Household Management**:  FOUNDATION - Backend complete, frontend components in progress
- **Shopping Lists**:  FOUNDATION - Backend complete, frontend components in progress
- **Meal Planning**:  FOUNDATION - Backend CRUD complete, unified plan page with roulette/exclusions/nutrition not yet implemented
- **Ingredient Substitution**:  NOT STARTED - Data model, three-scope rules, severity levels, and nutrition recalculation
- **Messaging System**:  PARTIAL - Backend complete, frontend inbox UI in progress

### Next Development Priorities

1. **Complete Frontend Components**: Household, shopping, and meal plan components
2. **Complete Curation Queue UI**: Finish admin interface
3. **Complete Messaging System**: Implement frontend messaging inbox
4. **Complete Multi-Participant Onboarding**: Finish remaining onboarding workflow
5. **Advanced Recipe Features**: Comments, ratings, assets, timeline, notes, tags, categories
6. **Integration Testing**: Comprehensive testing of all Mealie integration features
7. **Performance Optimization**: Database migrations, indexing, and query optimization
