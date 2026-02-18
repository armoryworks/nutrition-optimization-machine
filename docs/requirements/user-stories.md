# User Stories

## Overview

This document defines the complete set of user stories for the Nutrition Optimization Machine (NOM) platform. Every functional page, component, navigation path, and user flow is covered. Stories are organized by feature domain and reference the actual routes, components, services, and models in the codebase.

### Story Format

Each story follows the format:
- **ID**: Unique identifier (US-{domain}-{number})
- **Role**: The user role performing the action (see `user-roles-personas.md`)
- **Story**: "As a {role}, I want to {action} so that {benefit}"
- **Route**: The Angular route path
- **Component**: The Angular component(s) involved
- **Acceptance Criteria**: Specific, testable conditions that must be met
- **Dependencies**: Other stories or systems this story depends on

### User Roles Reference

| Role | Description |
|------|-------------|
| Anonymous User | Unauthenticated visitor browsing public content |
| New User | Just registered, going through onboarding |
| Authenticated User | Logged-in user with basic access |
| Recipe Author | Authenticated user who creates recipes/ingredients |
| Plan Administrator | Creates and manages nutritional plans |
| Plan Member | Participates in an existing plan |
| Site-Wide Admin | Has `CanManageCuration` claim |
| User Role Manager | Has `CanManageUserRoles` claim |

---

## 1. App Shell & Global Navigation

### US-SHELL-001: Fixed Header with Brand and Search

**Role**: Any User
**Story**: As any user, I want a fixed header at the top of every page with the NOM brand logo, a search bar, and action icons so that I always have access to core navigation and search.

**Route**: All routes (rendered in `app.component.html`)
**Component**: `AppComponent`

**Acceptance Criteria**:
1. Header is always visible and does not scroll with page content
2. Brand section displays "NOM" logo text with the "O" in accent color, linking to `/home`
3. "Nutrition Optimization Machine" tagline is displayed next to the brand
4. Search bar is positioned between brand and actions, expanding to fill available space
5. Search input has placeholder text "Search recipes, ingredients..."
6. Pressing Enter in search bar triggers search functionality
7. Header uses `flex-shrink: 0` and does not use `position: fixed`

### US-SHELL-002: Theme Toggle

**Role**: Any User
**Story**: As any user, I want to toggle between dark and light themes so that I can use NOM in my preferred visual style.

**Route**: All routes
**Component**: `AppComponent` (uses `ThemeService`)

**Acceptance Criteria**:
1. Theme toggle button is always visible in the header actions area
2. Button shows `light_mode` icon when in dark theme, `dark_mode` icon when in light theme
3. Clicking the button immediately switches the theme
4. Aria label dynamically reads "Toggle light theme" or "Toggle dark theme"
5. Theme preference persists across sessions

### US-SHELL-003: Anonymous Header Actions

**Role**: Anonymous User
**Story**: As an anonymous user, I want to see a login icon in the header so that I can access the login popover.

**Route**: All routes (when not authenticated)
**Component**: `AppComponent`

**Acceptance Criteria**:
1. When not logged in, a person icon button is displayed in header actions
2. Clicking the person icon opens the login popover
3. No notification bell or user menu is shown for anonymous users
4. Aria label reads "Login"

### US-SHELL-004: Authenticated Header Actions

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see a notification bell and user menu in the header so that I can access notifications and navigate to all features.

**Route**: All routes (when authenticated)
**Component**: `AppComponent`

**Acceptance Criteria**:
1. Notification bell icon is displayed (placeholder for future functionality)
2. User menu is accessible via account circle icon
3. User menu contains navigation items: Home, Households, Shopping, Meal Plans, Messages, Recipes, Ingredient Search, Curated Plans
4. User menu contains account items: About, Edit Profile, Privacy Settings, Logout
5. Curation Queue menu item is only visible to users with `CanManageCuration` claim
6. Dividers separate navigation groups from account actions

### US-SHELL-005: Login Popover

**Role**: Anonymous User
**Story**: As an anonymous user, I want to log in via a popover dialog without leaving the current page so that I can quickly authenticate.

**Route**: Any route (when not authenticated)
**Component**: `AppComponent`, `LoginComponent`

**Acceptance Criteria**:
1. Clicking the login icon in the header opens a popover overlay
2. A backdrop overlay appears behind the popover
3. Clicking the backdrop closes the popover
4. The login form (`nom-login`) is rendered inside the popover
5. The login component emits `closeRequested` to dismiss the popover on success
6. Popover is only rendered when user is not logged in AND `showLoginPopover()` is true

### US-SHELL-006: Logout

**Role**: Authenticated User
**Story**: As a logged-in user, I want to log out from the user menu so that I can end my session securely.

**Route**: Any route
**Component**: `AppComponent`

**Acceptance Criteria**:
1. "Logout" menu item with logout icon is available in the user menu
2. Clicking logout ends the session and clears authentication state
3. User is redirected to the home page after logout
4. Login popover button replaces user menu after logout

### US-SHELL-007: Fixed Footer

**Route**: All routes
**Component**: `AppComponent`

**Role**: Any User
**Story**: As any user, I want a footer at the bottom of every page showing attribution and links so that I can find project information.

**Acceptance Criteria**:
1. Footer is always pinned to the bottom of the viewport
2. Footer does not scroll with page content (uses `flex-shrink: 0`)
3. Left section displays "Powered by Mealie" with GitHub link
4. Right section displays current year and "NOM" with GitHub link
5. GitHub links open in new tabs with `rel="noopener noreferrer"`

### US-SHELL-008: Skip Link for Accessibility

**Role**: Any User (keyboard/screen reader)
**Story**: As a keyboard user, I want a "Skip to main content" link so that I can bypass the header navigation.

**Route**: All routes
**Component**: `AppComponent`

**Acceptance Criteria**:
1. Skip link is the first focusable element on the page
2. Skip link targets `#main-content`
3. Skip link is visually hidden until focused
4. Activating the link moves focus to the main content area

### US-SHELL-009: Viewport Layout with Independent Scrolling

**Role**: Any User
**Story**: As any user, I want the main content area and sidebar to scroll independently from the header and footer so that navigation controls are always accessible.

**Route**: All routes
**Component**: `AppComponent`

**Acceptance Criteria**:
1. `:host` uses `height: 100vh; overflow: hidden; display: flex; flex-direction: column`
2. Header and footer use `flex-shrink: 0` (never scroll)
3. Main content area uses `flex: 1; min-height: 0; overflow: hidden`
4. Within `.nom-app-layout`, main column and sidebar each have `overflow-y: auto`
5. Scrolling in the main column does not scroll the sidebar and vice versa

### US-SHELL-010: Sidebar Panel (Authenticated)

**Role**: Authenticated User
**Story**: As a logged-in user, I want a sidebar panel showing contextual information (upcoming meals, shopping lists, household, quick actions, restrictions) so that I have quick access to key data from any page.

**Route**: All routes (when authenticated)
**Component**: `ContextSidebarComponent`, `SidebarUpcomingMealsComponent`, `SidebarShoppingListsComponent`, `SidebarHouseholdContextComponent`, `SidebarQuickActionsComponent`, `SidebarRestrictionsComponent`

**Acceptance Criteria**:
1. Sidebar is only rendered for authenticated users
2. Sidebar contains 5 panel cards in order: Upcoming Meals, Shopping Lists, Households, Quick Actions, Dietary Restrictions
3. Each panel has a card-styled container with title and icon
4. Sidebar occupies 520px width on screens wider than 1280px
5. Sidebar scrolls independently from main content
6. Anonymous users see a single-column layout with no sidebar

### US-SHELL-011: Sidebar Sidecar Collapse (Responsive)

**Role**: Authenticated User
**Story**: As a logged-in user on a narrow screen, I want the sidebar to collapse into a slide-out panel so that the main content has full width.

**Route**: All routes (when authenticated, below 1280px)
**Component**: `AppComponent`

**Acceptance Criteria**:
1. Below 1280px breakpoint, the sidebar collapses and is hidden
2. A "Panel" toggle button appears fixed to the right edge of the screen
3. Clicking the toggle button slides the sidebar in from the right as an overlay
4. A semi-transparent backdrop appears behind the sidebar overlay
5. Clicking the backdrop or the "Close Panel" button inside the sidebar closes it
6. Sidebar uses `transform: translateX` with a 0.25s ease transition
7. Sidebar overlay has `z-index: $z-modal` and backdrop has `z-index: $z-modal-backdrop`

### US-SHELL-012: Sidebar — Upcoming Meals Panel

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see my upcoming meals for the next 3 days in the sidebar so that I can quickly reference my meal plan.

**Component**: `SidebarUpcomingMealsComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Panel title shows "Upcoming Meals" with a cutlery icon
2. Displays TODAY, TOMORROW, and the next day's name (e.g., "WEDNESDAY")
3. Each day shows meal entries (breakfast, lunch, dinner) with recipe names
4. Days with no meals show "No meals planned" in muted text
5. "View all meal plans" link at bottom navigates to `/meal-plan`
6. Panel shows loading state while fetching data
7. Panel handles errors gracefully with a retry option

### US-SHELL-013: Sidebar — Shopping Lists Panel

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see my active shopping lists in the sidebar so that I can track shopping progress.

**Component**: `SidebarShoppingListsComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Panel title shows "Shopping Lists" with a shopping cart icon
2. Displays active shopping lists with names and item counts
3. Each list shows a progress bar (completed items / total items)
4. Empty state shows "No active shopping lists"
5. "View all lists" link at bottom navigates to `/shopping`
6. Clicking a list navigates to its detail page

### US-SHELL-014: Sidebar — Households Panel

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see my households in the sidebar so that I can quickly see household context and membership.

**Component**: `SidebarHouseholdContextComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Panel title shows "Households" with a groups icon
2. Displays each household's name and member count
3. Shows owner badge for households the user owns
4. "Manage households" link at bottom navigates to `/household`
5. Empty state shows "No households" with a create link

### US-SHELL-015: Sidebar — Quick Actions Panel

**Role**: Authenticated User
**Story**: As a logged-in user, I want a grid of quick action buttons in the sidebar so that I can rapidly navigate to common tasks.

**Component**: `SidebarQuickActionsComponent`

**Acceptance Criteria**:
1. Panel title shows "Quick Actions" with a lightning bolt icon
2. 2-column grid of navigation tiles
3. Tiles include: New Meal Plan (`/meal-plan/create`), New Recipe (`/recipes/new`), Shopping List (`/shopping/create`), Search Recipes (`/recipes/search`), Household (`/household`), Restrictions (`/restrictions`)
4. Each tile shows an icon and label
5. Clicking a tile navigates to the corresponding route

### US-SHELL-016: Sidebar — Dietary Restrictions Panel

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see my dietary restrictions in the sidebar so that I can quickly reference my dietary needs.

**Component**: `SidebarRestrictionsComponent`
**Service**: `PersonService`

**Acceptance Criteria**:
1. Panel title shows "Dietary Restrictions" with a restrictions icon
2. Displays current person's name and list of restriction chips
3. Each chip shows the restriction type name
4. Empty state shows "No restrictions set" with a link to manage restrictions
5. Person switcher available if household has multiple people

---

## 2. Authentication

### US-AUTH-001: User Registration

**Role**: Anonymous User
**Story**: As a new visitor, I want to create an account with my email and password so that I can access NOM's features.

**Route**: `/register`
**Component**: `RegistrationComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Registration form displays fields: Email Address (required), Full Name (optional), Password (required), Confirm Password (required)
2. Email field validates email format
3. Password requirements are displayed and validated in real-time:
   - At least 8 characters
   - At least one uppercase letter (A-Z)
   - At least one lowercase letter (a-z)
   - At least one number (0-9)
   - At least one special character (!@#$...)
4. Each requirement shows a visual indicator (met/unmet)
5. Confirm Password must match Password
6. "Register Account" button is disabled until all validations pass
7. "Already have an account? Login" link navigates to login
8. On successful registration, user is automatically logged in and redirected to onboarding
9. On failure, error message is displayed (e.g., "Email already in use")
10. Page uses form-card stereotype with 600px max-width

### US-AUTH-002: User Login (Popover)

**Role**: Anonymous User
**Story**: As a returning user, I want to log in via the header popover so that I can quickly access my account.

**Route**: Any route (popover overlay)
**Component**: `LoginComponent` (within `AppComponent` popover)
**Service**: `AuthService`

**Acceptance Criteria**:
1. Login form displays Email and Password fields
2. "Login" submit button authenticates against the API
3. On success, popover closes and UI updates to authenticated state (user menu appears)
4. On failure, error message is displayed
5. "Forgot Password?" link navigates to `/forgot-password`
6. "Create Account" link navigates to `/register`

### US-AUTH-003: Forgot Password

**Role**: Anonymous User
**Story**: As a user who forgot my password, I want to request a password reset email so that I can regain access to my account.

**Route**: `/forgot-password`
**Component**: `ForgotPasswordComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Form displays Email Address field
2. Submit button sends password reset email
3. Success message confirms email was sent
4. Error handling for unregistered email
5. Link back to login

### US-AUTH-004: Reset Password

**Role**: Anonymous User (via email link)
**Story**: As a user who requested a password reset, I want to set a new password using the reset link so that I can access my account again.

**Route**: `/reset-password`
**Component**: `ResetPasswordComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Form displays New Password and Confirm Password fields
2. Password requirements are validated (same as registration)
3. Reset token from email link is submitted with the new password
4. On success, user is redirected to login
5. Expired or invalid tokens show an error message

### US-AUTH-005: Email Confirmation

**Role**: New User (via email link)
**Story**: As a newly registered user, I want to confirm my email address by clicking the confirmation link so that my account is verified.

**Route**: `/confirm-email`
**Component**: `ConfirmEmailComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Page processes confirmation token from URL parameters
2. On success, displays confirmation message with link to login
3. On failure (expired/invalid token), displays error with option to resend

### US-AUTH-006: Resend Confirmation Email

**Role**: New User
**Story**: As a user whose confirmation link expired, I want to request a new confirmation email so that I can verify my account.

**Route**: `/send-confirmation`
**Component**: `SendConfirmationEmailComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Form displays Email Address field
2. Submit button sends new confirmation email
3. Success message confirms email was sent
4. Handles already-confirmed accounts gracefully

### US-AUTH-007: Update Account Info

**Role**: Authenticated User
**Story**: As a logged-in user, I want to update my account information (email, password) so that my credentials stay current.

**Route**: `/update-info`
**Component**: `UpdateInfoComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Form displays current email and option to change
2. Password change requires current password and new password
3. Password requirements validated in real-time
4. Changes require re-authentication

### US-AUTH-008: Two-Factor Authentication Setup

**Role**: Authenticated User
**Story**: As a security-conscious user, I want to enable two-factor authentication so that my account has an extra layer of security.

**Route**: `/update-two-factor`
**Component**: `UpdateTwoFactorComponent`
**Service**: `AuthService`

**Acceptance Criteria**:
1. Page displays QR code for authenticator app setup
2. Verification code input field to confirm setup
3. Recovery codes are displayed after successful setup
4. Option to disable 2FA if already enabled
5. Clear instructions for supported authenticator apps

---

## 3. Onboarding

### US-ONB-001: Onboarding Wizard Entry

**Role**: New User
**Story**: As a newly registered user, I want to be guided through an onboarding wizard so that my profile and preferences are set up.

**Route**: `/onboarding` (redirects to `/onboarding/invitationCode`)
**Component**: `OnboardingWorkflowComponent`
**Service**: `OnboardingService`, `QuestionService`

**Acceptance Criteria**:
1. User is redirected to onboarding after registration
2. Wizard displays the first step (invitation code)
3. Navigation controls (Next, Back, Skip) are available
4. Progress indicator shows current step
5. Wizard maintains state across step navigation (data preserved on back/forward)

### US-ONB-002: Invitation Code Step

**Role**: New User
**Story**: As a new user, I want to enter an invitation code to join an existing plan so that I can collaborate with others.

**Route**: `/onboarding/invitationCode`
**Component**: `OnboardingWorkflowComponent`

**Acceptance Criteria**:
1. Displays "Join an Existing Plan (Optional)" heading
2. Invitation Code text input field
3. "Join Plan" button validates the code
4. "I have no Invitation Code" button skips this step
5. Invalid code shows error message
6. Valid code links user's Person record to the plan

### US-ONB-003: Participant Setup

**Role**: New User
**Story**: As a new user, I want to add additional participants (family members) so that the plan accommodates everyone's dietary needs.

**Route**: `/onboarding/participants`
**Component**: `OnboardingWorkflowComponent`

**Acceptance Criteria**:
1. Asks "Are there additional participants?"
2. Allows specifying number of additional participants
3. Provides name input slots for each participant
4. Each participant can have individual dietary preferences
5. Participants are stored as Person records in the system

### US-ONB-004: Dietary Restriction Collection

**Role**: New User
**Story**: As a new user, I want to specify my dietary restrictions during onboarding so that my meal plans are personalized.

**Route**: `/onboarding/{restrictionStep}`
**Component**: `OnboardingWorkflowComponent`

**Acceptance Criteria**:
1. Presents predefined restriction categories:
   - Societal/Religious/Ethical (Kosher, Vegan, Halal, etc.)
   - Allergies/Medical (Celiac, Diabetes, nut allergies, etc.)
   - Personal Preferences (spice levels, disliked ingredients, textures)
2. Restrictions can be assigned to a specific person or the entire plan
3. Multiple restrictions can be selected per category
4. Restrictions are deduplicated (same type cannot be applied twice per context)
5. Navigation back preserves selections

### US-ONB-005: Onboarding Summary & Submission

**Role**: New User
**Story**: As a new user, I want to review and submit all my onboarding data at once so that my profile is created efficiently.

**Route**: `/onboarding/summary`
**Component**: `OnboardingWorkflowComponent`

**Acceptance Criteria**:
1. Summary displays all collected data: profile info, participants, restrictions
2. "Submit" button sends all data in a single API call
3. Loading indicator during submission
4. On success, redirect to home page
5. On failure, error message with option to retry
6. Collected data is aggregated into `OnboardingCompleteRequestModel`

---

## 4. Home Page

### US-HOME-001: Anonymous Home Page — Browse Recipes by Meal Category

**Role**: Anonymous User
**Story**: As a visitor, I want to land on a page showing popular recipes organized by meal category so that I can immediately browse what NOM offers without signing up.

**Route**: `/home`
**Component**: `Home`
**Service**: `RecipeSearchService` (via `GET /api/recipe-search/popular`)

**Acceptance Criteria**:
1. Page fetches popular recipes from the API and groups them by category
2. Recipes displayed in horizontal rows grouped by meal category (categories derived from recipe data)
3. Each category section has a heading with an icon and category name
4. Recipe cards display: image (or placeholder icon if no imageUrl), recipe name, total time, and rating (if rated)
5. Clicking a recipe card navigates to `/recipe/{id}` (numeric ID) for the full recipe detail
6. No sidebar is visible (single-column layout)
7. The header search bar is available for searching recipes by keyword
8. A "Sign In" button is visible in the header for users who want to authenticate

### US-HOME-002: Authenticated Home Page

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see the same recipe browsing experience with my sidebar panel so that I can discover recipes while having quick access to my meal plans, shopping lists, and actions.

**Route**: `/home`
**Component**: `HomeComponent`

**Acceptance Criteria**:
1. Same recipe-by-category layout as anonymous view (Breakfast, Lunch, Dinner, Dessert, Snacks)
2. Sidebar panel is visible with contextual information (upcoming meals, shopping lists, quick actions)
3. Header shows authenticated controls (notifications, user avatar) instead of "Sign In"
4. Recipe content fills the main column alongside the sidebar

### US-HOME-003: Recipe Display by Meal Category

**Role**: Any User
**Story**: As any user, I want community recipes organized by meal category so that I can browse by the type of meal I'm looking for.

**Route**: `/home`
**Component**: `Home`
**Service**: `RecipeSearchService` (via `GET /api/recipe-search/popular`)

**Acceptance Criteria**:
1. Recipes are fetched from `RecipeSearchService.getPopular()` and grouped client-side by category
2. Categories displayed in order: Breakfast (`egg_alt` icon), Lunch (`lunch_dining` icon), Dinner (`dinner_dining` icon), Dessert (`cake` icon), Snacks (`cookie` icon)
3. Categories with no recipes are not displayed
4. Recipe cards display: image (or placeholder if no imageUrl), recipe name, total time (with clock icon), and star rating
5. Recipe cards are in a responsive grid (4 columns at desktop, 2 at tablet, 1 at mobile)
6. Clicking a recipe card navigates to `/recipe/{id}` (numeric ID from API)
7. Cards have subtle hover effect (translateY + shadow) for interactivity feedback

### US-HOME-004: Recipe Search via Header

**Role**: Any User
**Story**: As any user, I want to search for recipes using the search bar in the header so that I can find specific recipes without leaving the browsing experience.

**Route**: `/home` (search triggers navigation to `/search?q={query}`)
**Component**: `HeaderComponent`, `Search`

**Acceptance Criteria**:
1. Search bar is visible in the header for all users (anonymous and authenticated)
2. Placeholder text is context-dependent:
   - Anonymous users see: "Search recipes..."
   - Authenticated users see: "Search recipes, ingredients, plans..."
3. Pressing Enter submits the search and navigates to `/search?q={query}`
4. Search works for both anonymous and authenticated users
5. Search bar is hidden on mobile viewports (below mobile breakpoint)

### US-HOME-005: Loading, Error, and Empty States

**Role**: Any User
**Story**: As any user, I want appropriate feedback when recipes are loading, fail to load, or are empty so that I understand the current state.

**Route**: `/home`
**Component**: `HomeComponent`

**Acceptance Criteria**:
1. Loading state shows inline loading indicator with "Loading recipes..."
2. Error state shows error icon, error message text, and "Try Again" button
3. Empty state shows restaurant icon, "No recipes yet" heading, "Recipes will appear here once the community starts sharing." message, and "Browse Recipe Search" button

---

## 5. Recipe Management

### US-RCP-001: Recipe Author Dashboard

**Role**: Recipe Author
**Story**: As a recipe author, I want a dashboard showing all my recipes and ingredients organized by curation status so that I can manage my content.

**Route**: `/recipes`
**Component**: `RecipeAuthorDashboardComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Dashboard header with "My Recipes" title
2. "Create Recipe" action button navigating to `/recipes/new`
3. Recipe list showing name, status badge (NonCurated, PendingCuration, Curated, Rejected), creation date
4. Filter/sort options by curation status
5. Click on recipe navigates to `/recipes/{id}`
6. Empty state when no recipes exist
7. Dashboard stereotype layout

### US-RCP-002: Create Recipe

**Role**: Recipe Author
**Story**: As a recipe author, I want to create a new recipe with name, description, ingredients, and step-by-step instructions so that I can share it with the community.

**Route**: `/recipes/new`
**Component**: `RecipeEditComponent`
**Service**: `RecipeService`, `RecipeSearchService`

**Acceptance Criteria**:
1. Form includes: Recipe Name (required), Description, Servings, Prep Time, Cook Time, Total Time
2. Ingredient section allows searching and selecting existing ingredients
3. Ingredient search queries the curated ingredient database in real-time
4. Each ingredient entry includes: ingredient reference, quantity, measurement unit, optional notes
5. Step section allows adding numbered preparation steps with text instructions
6. Ingredients and steps support drag-drop reordering
7. "Save" button creates the recipe with `CurationStatus: NonCurated`
8. Validation prevents saving without required fields
9. Form-card stereotype layout

### US-RCP-003: Edit Recipe

**Role**: Recipe Author
**Story**: As a recipe author, I want to edit my existing recipes so that I can improve and refine them.

**Route**: `/recipes/{id}/edit`
**Component**: `RecipeEditComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Form is pre-populated with existing recipe data
2. All fields are editable (same fields as create)
3. Editing a `NonCurated` or `Rejected` recipe updates it in place
4. Editing a `Curated` recipe creates a new version (version number incremented, linked via `ParentRecipeId`)
5. Save button submits the changes
6. Cancel button returns to recipe detail without saving

### US-RCP-004: View Recipe Detail

**Role**: Recipe Author
**Story**: As a recipe author, I want to view the full details of my recipe so that I can review it before submission.

**Route**: `/recipes/{id}`
**Component**: `RecipeEditComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Displays recipe name, description, servings, prep/cook/total time
2. Lists all ingredients with quantities and measurements
3. Lists all preparation steps in order
4. Shows curation status badge
5. "Edit" button navigates to edit view
6. "Submit for Curation" button available for `NonCurated` recipes
7. Shows version history if recipe has versions

### US-RCP-005: Public Recipe Detail

**Role**: Anonymous User
**Story**: As a visitor, I want to view a recipe's full details via a shareable URL so that I can browse community recipes and share links with others.

**Route**: `/recipe/:id`
**Component**: `RecipeDetail`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Recipe URL includes the numeric recipe ID (e.g., `/recipe/42`) so URLs are shareable
2. Recipe data fetched from `RecipeService.getRecipe(id)` via `GET /api/recipe/{id}` (anonymous access for public/approved recipes)
3. "Back to recipes" link with arrow icon returns to `/home`
4. Hero image displayed at full width if `imageUrl` is present (300px height, `object-fit: cover`)
5. Header section displays: recipe name (h1), description, and meta chips:
   - Prep time (clock icon + minutes)
   - Cook time (fire icon + minutes)
   - Servings (restaurant icon + count with singular/plural)
   - Rating (star icon + rating value and count, if rated)
   - Author (person icon + authorName, styled as secondary-container chip)
6. Ingredients section with checklist icon, shows quantity + measurement + name + optional notes
7. Instructions section with numbered list icon, ordered step descriptions
8. Sections use card styling: `surface-container-low` background, outline border, `border-radius-lg`
9. Loading state shows spinner while fetching from API
10. Error state shows error icon and "Browse Recipes" fallback link
11. Not-found state (404) shows `search_off` icon, "Recipe not found" heading, and "Browse Recipes" button linking to `/home`
12. Does not show edit or curation actions for anonymous users

### US-RCP-006: Recipe Search (Public)

**Role**: Any User
**Story**: As any user, I want to search for recipes by keyword so that I can find specific recipes by name, category, or description.

**Route**: `/search?q={query}`
**Component**: `Search`
**Service**: `RecipeSearchService`

**Acceptance Criteria**:
1. Search query is read from the `q` query parameter (e.g., `/search?q=chicken`)
2. Search calls `RecipeSearchService.search()` via `POST /api/recipe-search/search` (anonymous allowed)
3. When no query is provided, falls back to `RecipeSearchService.getPopular()` (browse mode)
4. Header shows contextual title: "Results for \"{query}\"" when searching, "All Recipes" when browsing
5. Total count from API response is displayed next to the title (e.g., "12 recipes found")
6. Results displayed in a responsive card grid: `repeat(auto-fill, minmax(300px, 1fr))`
7. Each result card is a horizontal layout with:
   - Recipe thumbnail image (140px width) or placeholder icon if no imageUrl
   - Info section: first category label (uppercase, primary color), recipe name, description (2-line clamp), meta (total time + star rating)
8. Cards link to `/recipe/:id` (numeric ID) for full recipe detail
9. Cards have hover effect: `translateY(-2px)` + elevated shadow
10. Loading state shows spinner while API request is in flight
11. Error state shows error icon, message, and "Try Again" button
12. Empty result state shows `search_off` icon, "No recipes found", "Try a different search term", and link to browse all recipes
13. Search works without authentication
14. Future: pagination, advanced filters (categories, tags, ingredients, cook time)

### US-RCP-007: Submit Recipe for Curation

**Role**: Recipe Author
**Story**: As a recipe author, I want to submit my recipe for admin review so that it can become a curated community recipe.

**Route**: `/recipes/{id}` (action on detail page)
**Component**: `RecipeEditComponent`
**Service**: `CurationService`

**Acceptance Criteria**:
1. "Submit for Curation" button changes status from `NonCurated` to `PendingCuration`
2. Button is only available for `NonCurated` recipes
3. All recipe ingredients must exist (ingredient references valid)
4. Confirmation dialog before submission
5. Status badge updates after successful submission
6. Recipe becomes read-only while `PendingCuration`

### US-RCP-008: Create Ingredient

**Role**: Recipe Author
**Story**: As a recipe author, I want to create a custom ingredient when the one I need doesn't exist in the database so that I can complete my recipe.

**Route**: `/recipes/ingredients/new`
**Component**: `IngredientEditComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Form includes: Ingredient Name (required), Description, Nutritional Data
2. Real-time duplicate checking shows similar existing ingredients
3. User is encouraged to use existing ingredients first (reuse-first workflow)
4. New ingredient is created with `CurationStatus: NonCurated`
5. Ingredient can be immediately used in recipes after creation
6. Form-card stereotype layout

### US-RCP-009: Edit Ingredient

**Role**: Recipe Author
**Story**: As a recipe author, I want to edit my custom ingredients so that I can correct or improve nutritional data.

**Route**: `/recipes/ingredients/{id}/edit`
**Component**: `IngredientEditComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Form pre-populated with existing ingredient data
2. Editing restricted to `NonCurated` or `Rejected` ingredients
3. `Curated` ingredients cannot be edited (immutable curation rule)
4. Save updates the ingredient
5. Cancel returns without saving

### US-RCP-010: Ingredient Search

**Role**: Authenticated User
**Story**: As a logged-in user, I want to search the ingredient database so that I can explore available ingredients and their nutritional information.

**Route**: `/ingredient-search`
**Component**: `IngredientSearchComponent`
**Service**: `RecipeService`, `RecipeSearchService`

**Acceptance Criteria**:
1. Search input with real-time results from 8,049 curated ingredients
2. Results show ingredient name, description, nutritional highlights
3. Paginated results
4. Click on ingredient shows full nutritional detail
5. Search stereotype layout

---

## 6. Curation Workflow

### US-CUR-001: Curation Queue

**Role**: Site-Wide Admin
**Story**: As an admin, I want to see a queue of all pending curation submissions so that I can review and manage content quality.

**Route**: `/curation`
**Component**: `CurationQueueComponent`
**Service**: `CurationService`

**Acceptance Criteria**:
1. Queue lists all `PendingCuration` recipes and ingredients
2. Each item shows: name, author, submission date, type (recipe/ingredient)
3. Items are sortable by submission date
4. Items are filterable by author
5. Clicking an item expands to show full detail for review
6. Route is only accessible to users with `CanManageCuration` claim
7. Empty state shows "No pending submissions"

### US-CUR-002: Approve Submission

**Role**: Site-Wide Admin
**Story**: As an admin, I want to approve a submission so that it becomes a curated community resource.

**Route**: `/curation` (action within queue)
**Component**: `CurationQueueComponent`
**Service**: `CurationService`

**Acceptance Criteria**:
1. "Approve" button changes status to `Curated`
2. Optional private notes field for internal documentation
3. Optional public notes field visible to the author
4. Recipe approval is blocked if any ingredients are not `Curated` (dependency validation)
5. Approved items are removed from the queue
6. Author is notified of approval (via messaging system)

### US-CUR-003: Reject Submission

**Role**: Site-Wide Admin
**Story**: As an admin, I want to reject a submission with feedback so that the author understands what needs to change.

**Route**: `/curation` (action within queue)
**Component**: `CurationQueueComponent`
**Service**: `CurationService`

**Acceptance Criteria**:
1. "Reject" button changes status to `Rejected`
2. Feedback notes field is REQUIRED for rejection
3. Author can see rejection feedback
4. Rejected item is removed from the queue
5. Author can edit and resubmit rejected content

### US-CUR-004: Request Revision

**Role**: Site-Wide Admin
**Story**: As an admin, I want to request revisions on a submission so that the author can improve it before approval.

**Route**: `/curation` (action within queue)
**Component**: `CurationQueueComponent`
**Service**: `CurationService`

**Acceptance Criteria**:
1. "Request Revision" button changes status to `RequiresRevision`
2. Feedback notes field is REQUIRED
3. Item remains in queue with updated status
4. Author receives notification with revision feedback
5. Author can edit and resubmit

---

## 7. Household Management

### US-HH-001: Household Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see all my households so that I can manage my family and group memberships.

**Route**: `/household`
**Component**: `HouseholdDashboardComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Dashboard header with "Households" title
2. "Create Household" action button navigating to `/household/create`
3. List of user's households showing: name, description, member count, owner indicator
4. Click on household navigates to `/household/{id}`
5. Empty state with invitation to create first household
6. Dashboard stereotype layout

### US-HH-002: Create Household

**Role**: Authenticated User
**Story**: As a logged-in user, I want to create a new household so that I can organize meal planning for my family or group.

**Route**: `/household/create`
**Component**: `HouseholdCreateComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Form includes: Household Name (required), Description (optional)
2. "Create Household" button submits the form
3. "Cancel" button returns to dashboard
4. Creator is automatically added as household owner
5. On success, redirect to household detail
6. Validation errors displayed inline
7. Form-card stereotype layout

### US-HH-003: Household Detail

**Role**: Authenticated User (household member)
**Story**: As a household member, I want to view household details including members, stats, and settings so that I can understand the household context.

**Route**: `/household/{id}`
**Component**: `HouseholdDetailComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Displays household name, description, creation date
2. Member list with names, email addresses, roles, and join dates
3. Member count, recipe count, shopping list count stats
4. Owner badge displayed next to the owner
5. "Edit" button (owner only) navigates to `/household/{id}/edit`
6. "Invite Members" button (owner only) navigates to `/household/{id}/invite`
7. "Remove Member" action (owner only) with confirmation dialog
8. "Settings" link navigates to `/household/{id}/settings`

### US-HH-004: Edit Household

**Role**: Plan Administrator (household owner)
**Story**: As a household owner, I want to edit my household's name and description so that I can keep the information current.

**Route**: `/household/{id}/edit`
**Component**: `HouseholdEditComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Form pre-populated with current household data
2. Name and description fields are editable
3. Save updates the household
4. Cancel returns to detail without saving

### US-HH-005: Invite Members

**Role**: Plan Administrator (household owner)
**Story**: As a household owner, I want to invite new members via email so that they can join and collaborate on meal planning.

**Route**: `/household/{id}/invite`
**Component**: `HouseholdInviteComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Form includes email address field for invitation
2. System generates a secure invitation token
3. Invitation link can be copied to clipboard
4. Invitation has expiration date
5. Multiple invitations can be sent
6. Existing members cannot be re-invited

### US-HH-006: Join Household via Invite Token

**Role**: Authenticated User
**Story**: As an invited user, I want to join a household using an invitation link so that I can participate in group meal planning.

**Route**: `/household/join/{token}`
**Component**: `HouseholdJoinComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Page validates the invitation token
2. Displays household name and inviter information
3. "Join Household" button adds user as member
4. Invalid/expired tokens show an error message
5. Already-member users see appropriate message
6. On success, redirect to household detail

### US-HH-007: Household Settings

**Role**: Plan Administrator (household owner)
**Story**: As a household owner, I want to manage household settings including preferences, notifications, and privacy so that the household is configured properly.

**Route**: `/household/{id}/settings`
**Component**: `HouseholdSettingsComponent`
**Service**: `HouseholdService`

**Acceptance Criteria**:
1. Preferences section for dietary defaults
2. Notification settings for household activity
3. Privacy controls for data sharing
4. Save button persists settings
5. Only household owner can access

---

## 8. Shopping Lists

### US-SHOP-001: Shopping Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see all my shopping lists so that I can manage my grocery needs.

**Route**: `/shopping`
**Component**: `ShoppingDashboardComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Dashboard header with "Shopping Lists" title
2. "Create Shopping List" action button navigating to `/shopping/create`
3. List of shopping lists showing: name, item count, completion status, household association
4. Active/inactive toggle or filter
5. Click on list navigates to `/shopping/{id}`
6. Empty state with invitation to create first list
7. Dashboard stereotype layout

### US-SHOP-002: Create Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to create a new shopping list so that I can organize my grocery shopping.

**Route**: `/shopping/create`
**Component**: `ShoppingCreateComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Form includes: List Name (required), Description (optional), Household selection
2. "Create Shopping List" button submits the form
3. "Cancel" button returns to dashboard
4. On success, redirect to shopping list detail
5. Form-card stereotype layout

### US-SHOP-003: Shopping List Detail

**Role**: Authenticated User
**Story**: As a logged-in user, I want to view and manage items in my shopping list so that I can track what I need to buy.

**Route**: `/shopping/{id}`
**Component**: `ShoppingDetailComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Displays list name, description, and household context
2. Item list with: name, quantity, category, completion checkbox
3. Add item inline or via button
4. Check/uncheck items to mark as purchased
5. Delete individual items
6. Progress indicator (X of Y items completed)
7. "Edit" button navigates to `/shopping/{id}/edit`
8. Action buttons for: Add from Recipe, Bulk Edit, Share, Export

### US-SHOP-004: Edit Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to edit my shopping list's name and description so that I can keep it organized.

**Route**: `/shopping/{id}/edit`
**Component**: `ShoppingEditComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Form pre-populated with current list data
2. Name and description fields are editable
3. Save updates the list metadata
4. Cancel returns to detail without saving

### US-SHOP-005: Add Recipe Ingredients to Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to add all ingredients from a recipe to my shopping list so that I don't forget anything when shopping.

**Route**: `/shopping/{id}/recipes`
**Component**: `ShoppingRecipeIntegrationComponent`
**Service**: `ShoppingListService`, `RecipeService`

**Acceptance Criteria**:
1. Recipe search/selection interface
2. Selected recipe shows its ingredients
3. Serving size adjustment to scale quantities
4. Select/deselect individual ingredients
5. "Add to Shopping List" button adds selected ingredients
6. Duplicate ingredients are consolidated
7. Confirmation message after adding

### US-SHOP-006: Bulk Edit Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to perform bulk operations on shopping list items so that I can efficiently manage large lists.

**Route**: `/shopping/{id}/bulk-edit`
**Component**: `ShoppingBulkEditorComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Multi-select checkboxes on all items
2. Bulk actions: Complete All, Delete Selected, Categorize, Set Priority
3. Select All / Deselect All controls
4. Confirmation dialog for destructive actions
5. Updated item counts after operations

### US-SHOP-007: Share Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to share a shopping list with household members so that we can collaborate on shopping.

**Route**: `/shopping/{id}/share`
**Component**: `ShoppingListShareComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Displays household members available for sharing
2. Toggle sharing with individual members
3. Generate shareable link
4. View who currently has access
5. Revoke sharing access

### US-SHOP-008: Export Shopping List

**Role**: Authenticated User
**Story**: As a logged-in user, I want to export my shopping list to PDF, CSV, or email so that I can use it outside of NOM.

**Route**: `/shopping/{id}/export`
**Component**: `ShoppingListExportComponent`
**Service**: `ShoppingListService`

**Acceptance Criteria**:
1. Export format options: PDF, CSV, Email
2. PDF export generates a print-friendly document
3. CSV export creates a downloadable spreadsheet
4. Email export sends list to specified email address
5. Export includes item names, quantities, categories

### US-SHOP-009: Shopping Category Management

**Role**: Authenticated User
**Story**: As a logged-in user, I want to manage shopping categories (produce, dairy, etc.) so that my items are organized by store section.

**Route**: `/shopping/categories`
**Component**: `ShoppingCategoryManagementComponent`
**Service**: `ShoppingListCategoryService`

**Acceptance Criteria**:
1. List of shopping categories with color coding and icons
2. Create new category with name, color picker, icon selector
3. Edit existing categories
4. Delete categories (with item reassignment)
5. Drag-drop reordering of categories
6. Default categories provided

---

## 9. Meal Planning

### US-MP-001: Meal Plan Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see all my meal plans so that I can manage my weekly meal schedules.

**Route**: `/meal-plan`
**Component**: `MealPlanDashboardComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Dashboard header with "Meal Plans" title
2. "Create Meal Plan" action button navigating to `/meal-plan/create`
3. List of meal plans showing: recipe name, date, meal type, household
4. Click on plan navigates to detail
5. Empty state with invitation to create first plan
6. Dashboard stereotype layout

### US-MP-002: Create Meal Plan Entry

**Role**: Authenticated User
**Story**: As a logged-in user, I want to create a meal plan entry by selecting a recipe, date, and meal type so that I can schedule my meals.

**Route**: `/meal-plan/create`
**Component**: `MealPlanCreateComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Form includes: Recipe selection (searchable dropdown), Date picker, Meal Type (Breakfast/Lunch/Dinner/Snack), Notes
2. Recipe selection searches curated recipes
3. Date defaults to today
4. "Create" button submits the plan entry
5. "Cancel" button returns to dashboard
6. Validation ensures recipe and date are selected
7. Form-card stereotype layout

### US-MP-003: Meal Plan Calendar View

**Role**: Authenticated User
**Story**: As a logged-in user, I want a weekly calendar view of my meal plans so that I can see my entire week's meals at a glance.

**Route**: `/meal-plan/calendar`
**Component**: `MealPlanCalendarComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. 7-column grid showing days of the week
2. 4 meal slots per day (Breakfast, Lunch, Dinner, Snack)
3. Each slot shows the recipe name if assigned
4. Week navigation (previous/next week)
5. Click on empty slot navigates to create with date pre-filled
6. Click on filled slot navigates to plan detail
7. Current day highlighted
8. Calendar stereotype layout

### US-MP-004: Meal Plan Detail

**Role**: Authenticated User
**Story**: As a logged-in user, I want to view the details of a meal plan entry so that I can see the recipe and plan information.

**Route**: `/meal-plan/{id}`
**Component**: `MealPlanDetailComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Displays recipe name, date, meal type, notes
2. Link to full recipe detail
3. "Edit" button navigates to `/meal-plan/{id}/edit`
4. "Delete" button with confirmation
5. Nutritional summary for the planned meal

### US-MP-005: Edit Meal Plan Entry

**Role**: Authenticated User
**Story**: As a logged-in user, I want to edit a meal plan entry so that I can adjust my schedule.

**Route**: `/meal-plan/{id}/edit`
**Component**: `MealPlanEditComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Form pre-populated with existing plan data
2. Recipe, date, meal type, and notes are editable
3. Save updates the plan entry
4. Cancel returns to detail without saving

### US-MP-006: Meal Plan Rules

**Role**: Authenticated User
**Story**: As a logged-in user, I want to set meal plan rules (e.g., "no repeat recipes in a week") so that my meal plans have variety.

**Route**: `/meal-plan/rules`
**Component**: `MealPlanRulesComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. List of active rules with descriptions
2. Create new rule with: rule type, parameters, scope
3. Edit existing rules
4. Delete rules
5. Rules are applied during meal plan creation

### US-MP-007: Recipe Selection for Meal Plan

**Role**: Authenticated User
**Story**: As a logged-in user, I want to browse and select recipes for my meal plan so that I can choose from my available recipes.

**Route**: `/meal-plan/recipe-selection`
**Component**: `MealPlanRecipeSelectionComponent`
**Service**: `MealPlanService`, `RecipeService`

**Acceptance Criteria**:
1. Browsable list of available curated recipes
2. Search and filter functionality
3. Recipe cards with key details
4. "Add to Meal Plan" button with date/meal type selection
5. Quick-add workflow

### US-MP-008: Generate Shopping List from Meal Plan

**Role**: Authenticated User
**Story**: As a logged-in user, I want to generate a shopping list from my weekly meal plan so that I have all ingredients needed for the week.

**Route**: `/meal-plan/shopping-list` or `/meal-plan/{id}/shopping-list`
**Component**: `MealPlanToShoppingListComponent`
**Service**: `MealPlanService`, `ShoppingListService`

**Acceptance Criteria**:
1. Select date range (default: current week)
2. Preview consolidated ingredient list from all meals in range
3. Duplicate ingredients are merged with summed quantities
4. Option to add to existing shopping list or create new one
5. "Generate" button creates the shopping list
6. Confirmation with link to created shopping list

### US-MP-009: Print Meal Plan

**Role**: Authenticated User
**Story**: As a logged-in user, I want to print my weekly meal plan so that I can post it on my refrigerator.

**Route**: `/meal-plan/print` or `/meal-plan/{id}/print`
**Component**: `MealPlanPrintComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Print-friendly layout (no header/sidebar/footer)
2. Single plan view or weekly summary view
3. Displays recipe names, dates, meal types
4. PDF generation option
5. Browser print dialog integration

### US-MP-010: Weekly Nutrition Summary

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see the nutritional summary of my weekly meal plan so that I can track my dietary intake.

**Route**: `/meal-plan/nutrition` or `/meal-plan/{id}/nutrition`
**Component**: `MealPlanNutritionComponent`
**Service**: `MealPlanService`

**Acceptance Criteria**:
1. Macro breakdown (calories, protein, carbs, fat) per day and weekly total
2. Micro nutrient tracking (vitamins, minerals)
3. Visual charts/graphs for nutrient distribution
4. Comparison against recommended daily values
5. Per-meal and per-day breakdown

---

## 10. Communication & Messaging

### US-MSG-001: Messaging Inbox

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see my message inbox with all conversation threads so that I can manage my communications.

**Route**: `/messaging`
**Component**: `MessagingInboxComponent`
**Service**: `MessagingService`

**Acceptance Criteria**:
1. List of message threads showing: participant names, last message preview, timestamp, unread count
2. Threads sorted by most recent activity
3. Unread threads visually distinguished (bold/badge)
4. Search functionality to find threads
5. "New Conversation" button navigating to `/messaging/new`
6. Archive and pin thread actions
7. Click on thread navigates to `/messaging/thread/{id}`
8. Dashboard stereotype layout

### US-MSG-002: Compose New Message

**Role**: Authenticated User
**Story**: As a logged-in user, I want to start a new conversation with another user so that I can communicate about recipes, plans, or general topics.

**Route**: `/messaging/new`
**Component**: `MessageComposeComponent`
**Service**: `MessagingService`, `PersonService`

**Acceptance Criteria**:
1. Participant search field (searches persons in shared plans/households, or all users if admin)
2. Multiple participants can be selected
3. Optional thread context (link to recipe, ingredient, or plan)
4. Message body text input
5. "Send" button creates the thread and sends the message
6. Cancel returns to inbox
7. Form-card stereotype layout

### US-MSG-003: View Message Thread

**Role**: Authenticated User
**Story**: As a logged-in user, I want to view a conversation thread with full message history so that I can follow the discussion.

**Route**: `/messaging/thread/{id}`
**Component**: `MessageThreadDetailComponent`
**Service**: `MessagingService`

**Acceptance Criteria**:
1. Thread header shows participants and context (if linked to recipe/plan)
2. Message history displayed chronologically
3. Each message shows: sender name, avatar, message text, timestamp
4. Read/unread status indicators
5. Auto-scroll to most recent message
6. Reply input at bottom of thread
7. Send reply functionality

### US-MSG-004: Curation Feedback Thread

**Role**: Site-Wide Admin / Recipe Author
**Story**: As an admin, I want to send curation feedback to recipe authors via a messaging thread so that they can understand and address issues.

**Route**: `/messaging/thread/{id}` (CurationFeedback type)
**Component**: `MessageThreadDetailComponent`
**Service**: `MessagingService`

**Acceptance Criteria**:
1. Thread type is `CurationFeedback`
2. Thread is automatically linked to the recipe/ingredient context
3. Only admins can initiate curation feedback threads
4. Authors can reply but not initiate new curation threads
5. Context link navigates to the recipe/ingredient in question

---

## 11. Admin & User Management

### US-ADM-001: User Management Dashboard

**Role**: User Role Manager
**Story**: As a user role manager, I want to view and manage all system users so that I can assign appropriate permissions.

**Route**: `/admin/user-management`
**Component**: `UserManagementComponent`
**Service**: `UserManagementService`

**Acceptance Criteria**:
1. Searchable list of all system users
2. Each user shows: name, email, roles/claims, registration date
3. "Manage Roles" action for each user
4. Grant/revoke `CanManageCuration` claim
5. Grant/revoke `CanManageUserRoles` claim
6. Changes take effect immediately
7. Audit logging of all role changes
8. Route only accessible with `CanManageUserRoles` claim

---

## 12. Privacy & Data Management

### US-PRV-001: Privacy Settings Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to manage my privacy settings so that I control how my data is used.

**Route**: `/privacy-settings` or `/user/privacy-settings`
**Component**: `PrivacySettingsComponent`
**Service**: `PrivacyService`

**Acceptance Criteria**:
1. Current consent status for each data processing purpose
2. Toggle consent on/off for each purpose
3. Clear description of what each consent covers
4. Timestamp of last consent change
5. Link to full privacy policy
6. Data processing log viewer
7. Data subject rights actions (see below)

### US-PRV-002: Right of Access (Data Export)

**Role**: Authenticated User
**Story**: As a logged-in user, I want to export all my personal data so that I can see what NOM stores about me (GDPR Right of Access).

**Route**: `/privacy-settings` (action)
**Component**: `PrivacySettingsComponent`
**Service**: `PrivacyService`

**Acceptance Criteria**:
1. "Export My Data" button initiates data export
2. Export is processed asynchronously (background task)
3. User is notified when export is ready
4. Export includes all personal data, recipes, meal plans, restrictions, messages
5. Export format is machine-readable (JSON)
6. Data processing log records the export request

### US-PRV-003: Right to Erasure (Account Deletion)

**Role**: Authenticated User
**Story**: As a logged-in user, I want to delete my account and all personal data so that I can exercise my right to be forgotten (GDPR Right to Erasure).

**Route**: `/privacy-settings` (action)
**Component**: `PrivacySettingsComponent`
**Service**: `PrivacyService`

**Acceptance Criteria**:
1. "Delete My Account" button with strong confirmation (type account email)
2. Clear warning about irreversible action
3. Data is anonymized rather than hard-deleted (preserves referential integrity)
4. Personal identifiers are replaced with anonymous values
5. User is logged out after deletion
6. Data processing log records the deletion request

### US-PRV-004: Consent Withdrawal

**Role**: Authenticated User
**Story**: As a logged-in user, I want to withdraw specific consents so that I can limit how my data is processed.

**Route**: `/privacy-settings`
**Component**: `PrivacySettingsComponent`
**Service**: `PrivacyService`

**Acceptance Criteria**:
1. Each consent purpose has a toggle or revoke button
2. Withdrawal is effective immediately
3. System stops processing data for withdrawn purposes
4. Withdrawal timestamp is recorded
5. User can re-grant consent later

---

## 13. User Profile

### US-PROF-001: Edit Profile

**Role**: Authenticated User
**Story**: As a logged-in user, I want to edit my personal profile so that my information is accurate.

**Route**: `/edit-profile`
**Component**: `PersonProfileEditComponent`
**Service**: `PersonService`

**Acceptance Criteria**:
1. Form displays current profile information
2. Editable fields: name, health attributes, dietary preferences
3. Save updates the person record
4. Cancel returns to previous page
5. Validation on required fields
6. Form-card stereotype layout

### US-PROF-002: User Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want a personal dashboard showing my recipe activity so that I can track my contributions.

**Route**: `/user/dashboard`
**Component**: `RecipeAuthorDashboardComponent`
**Service**: `RecipeService`

**Acceptance Criteria**:
1. Shows user's recipes organized by curation status
2. Recipe count statistics
3. Recent activity feed
4. Quick links to create new recipe/ingredient

---

## 14. Cookbook Management

### US-CB-001: Cookbook Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to see all my cookbooks so that I can organize my recipe collections.

**Route**: `/cookbook`
**Component**: `CookbookDashboardComponent`
**Service**: `CookbookService`

**Acceptance Criteria**:
1. List of cookbooks with name, description, recipe count
2. "Create Cookbook" action button
3. Click on cookbook navigates to detail
4. Empty state
5. Dashboard stereotype layout

### US-CB-002: Create Cookbook

**Role**: Authenticated User
**Story**: As a logged-in user, I want to create a cookbook so that I can group related recipes together.

**Route**: `/cookbook/create`
**Component**: `CookbookCreateComponent`
**Service**: `CookbookService`

**Acceptance Criteria**:
1. Form with: name (required), description
2. Create button submits
3. Cancel returns to dashboard
4. Form-card stereotype layout

### US-CB-003: Cookbook Detail

**Role**: Authenticated User
**Story**: As a logged-in user, I want to view a cookbook's recipes so that I can browse my collection.

**Route**: `/cookbook/{id}`
**Component**: `CookbookDetailComponent`
**Service**: `CookbookService`

**Acceptance Criteria**:
1. Displays cookbook name and description
2. List of recipes in the cookbook
3. Add/remove recipes functionality
4. Edit and delete cookbook actions

### US-CB-004: Edit Cookbook

**Role**: Authenticated User
**Story**: As a logged-in user, I want to edit a cookbook so that I can update its information and contents.

**Route**: `/cookbook/{id}/edit`
**Component**: `CookbookEditComponent`
**Service**: `CookbookService`

**Acceptance Criteria**:
1. Form pre-populated with existing data
2. Editable name and description
3. Save/cancel actions

---

## 15. Curated Plans

### US-CP-001: Browse Curated Plans

**Role**: Authenticated User
**Story**: As a logged-in user, I want to browse pre-made curated meal plans so that I can adopt proven meal strategies.

**Route**: `/curated-plans`
**Component**: `CuratedPlansComponent`
**Service**: `PlanService`

**Acceptance Criteria**:
1. List of available curated plans with name, description, duration
2. Search/filter functionality
3. Click on plan shows full detail
4. "Clone Plan" action to copy a curated plan for personal use
5. Cloned plan can be customized

---

## 16. Webhook Management

### US-WH-001: Webhook Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to manage webhooks so that I can integrate NOM with external services.

**Route**: `/webhook`
**Component**: `WebhookDashboardComponent`
**Service**: `WebhookService`

**Acceptance Criteria**:
1. List of configured webhooks with URL, event type, status
2. Create new webhook (URL, event triggers, headers)
3. Edit existing webhooks
4. Delete webhooks
5. "Test" button sends a test payload
6. Activity log showing recent webhook deliveries

---

## 17. Label Management

### US-LBL-001: Label Dashboard

**Role**: Authenticated User
**Story**: As a logged-in user, I want to manage labels so that I can tag and categorize my items.

**Route**: `/labels`
**Component**: `LabelDashboardComponent`
**Service**: `LabelService`

**Acceptance Criteria**:
1. List of labels with name and usage count
2. Create new label with name and optional color
3. Edit existing labels
4. Delete labels (with reassignment options)
5. Labels usable across recipes, shopping items, and other entities

---

## 18. Measurement Management

### US-MSR-001: Measurement Units

**Role**: Authenticated User
**Story**: As a logged-in user, I want to manage measurement units and categories so that recipes use consistent measurements.

**Route**: (Accessible via recipe creation flow)
**Component**: Measurement components
**Service**: `MeasurementService`, `MeasurementCategoryService`

**Acceptance Criteria**:
1. List of measurement categories (volume, weight, count, etc.)
2. List of units within each category
3. Conversion relationships between units
4. Create custom measurements
5. Used in recipe ingredient quantity specification

---

## 19. Cross-Cutting Concerns

### US-CC-001: Loading States

**Role**: Any User
**Story**: As any user, I want visual feedback during data loading so that I know the system is working.

**Acceptance Criteria**:
1. `AmwInlineLoading` component used for content loading
2. `AmwFullScreenLoading` used for page transitions or heavy operations
3. Loading messages are contextual ("Loading recipes...", "Saving...", etc.)
4. Loading state prevents duplicate submissions (buttons disabled)

### US-CC-002: Error States

**Role**: Any User
**Story**: As any user, I want clear error messages when something goes wrong so that I can understand and recover.

**Acceptance Criteria**:
1. Error messages are user-friendly (not raw technical errors)
2. Error states include a retry action where applicable
3. Form validation errors are displayed inline next to the field
4. API errors are caught and displayed via notification service
5. Network errors show connection issue message

### US-CC-003: Empty States

**Role**: Any User
**Story**: As any user, I want helpful empty states when there's no data so that I know what to do next.

**Acceptance Criteria**:
1. Empty states include an icon, title, description, and action button
2. Action button guides user to create their first item
3. Empty state styling uses `nom-dashboard__empty` pattern
4. Messages are encouraging and instructional

### US-CC-004: Form Validation

**Role**: Any User
**Story**: As any user, I want real-time form validation so that I can correct errors before submitting.

**Acceptance Criteria**:
1. Required fields are marked with asterisk
2. Validation errors appear as the user interacts with fields
3. Submit button is disabled when form is invalid
4. Validation tooltip overlays provide additional context
5. Both client-side and server-side validation are applied

### US-CC-005: Dark/Light Theme

**Role**: Any User
**Story**: As any user, I want the entire application to correctly render in both dark and light themes.

**Acceptance Criteria**:
1. All pages render correctly in dark theme
2. All pages render correctly in light theme
3. Text contrast meets WCAG AA standards in both themes
4. Material Design 3 semantic tokens are used (not hardcoded colors)
5. Theme switch is instantaneous with no flash of unstyled content

### US-CC-006: Responsive Layout

**Role**: Any User
**Story**: As any user, I want the application to work on different screen sizes so that I can use it on various devices.

**Acceptance Criteria**:
1. Above 1280px: two-column layout with sidebar (when authenticated)
2. Below 1280px: single-column layout with sidecar sidebar
3. Form cards maintain max-width of 600px and center on screen
4. Dashboard grids collapse to fewer columns on narrow screens
5. Header actions remain accessible at all widths

---

## 20. Data-Testid Coverage

### US-TEST-001: Testability

**Role**: Developer/QA
**Story**: As a developer, I want all interactive elements to have `data-testid` attributes so that E2E tests can reliably target them.

**Acceptance Criteria**:
1. All form inputs have `data-testid` attributes
2. All submit/action buttons have `data-testid` attributes
3. All navigation links have `data-testid` attributes where appropriate
4. Key display elements (dashboards, cards, lists) have `data-testid` attributes
5. `data-testid` follows the pattern: `{page}-{element}-{qualifier}` (e.g., `home-get-started-btn`, `register-email-input`)

---

## Appendix A: Route Map

| Route | Component | Auth Required | Stereotype |
|-------|-----------|---------------|------------|
| `/` | Redirect to `/home` | No | - |
| `/home` | Home | No | Dashboard |
| `/about` | HomeComponent | No | Landing |
| `/register` | RegistrationComponent | No (NoAuthGuard) | Form-Card |
| `/forgot-password` | ForgotPasswordComponent | No | Form-Card |
| `/reset-password` | ResetPasswordComponent | No | Form-Card |
| `/confirm-email` | ConfirmEmailComponent | No | Form-Card |
| `/send-confirmation` | SendConfirmationEmailComponent | No | Form-Card |
| `/search` | Search | No | Search |
| `/recipe/:id` | RecipeDetail | No | Detail |
| `/onboarding` | OnboardingWorkflowComponent | Yes | Wizard |
| `/onboarding/:stepId` | OnboardingWorkflowComponent | Yes | Wizard |
| `/recipes` | RecipeAuthorDashboardComponent | Yes | Dashboard |
| `/recipes/new` | RecipeEditComponent | Yes | Form-Full |
| `/recipes/:id` | RecipeEditComponent | Yes | Detail |
| `/recipes/:id/edit` | RecipeEditComponent | Yes | Form-Full |
| `/recipes/ingredients/new` | IngredientEditComponent | Yes | Form-Card |
| `/recipes/ingredients/:id/edit` | IngredientEditComponent | Yes | Form-Card |
| `/household` | HouseholdDashboardComponent | Yes | Dashboard |
| `/household/create` | HouseholdCreateComponent | Yes | Form-Card |
| `/household/join/:token` | HouseholdJoinComponent | Yes | Form-Card |
| `/household/:id` | HouseholdDetailComponent | Yes | Detail |
| `/household/:id/edit` | HouseholdEditComponent | Yes | Form-Card |
| `/household/:id/invite` | HouseholdInviteComponent | Yes | Form-Card |
| `/household/:id/settings` | HouseholdSettingsComponent | Yes | Settings |
| `/shopping` | ShoppingDashboardComponent | Yes | Dashboard |
| `/shopping/create` | ShoppingCreateComponent | Yes | Form-Card |
| `/shopping/categories` | ShoppingCategoryManagementComponent | Yes | Settings |
| `/shopping/:id` | ShoppingDetailComponent | Yes | Detail |
| `/shopping/:id/edit` | ShoppingEditComponent | Yes | Form-Card |
| `/shopping/:id/recipes` | ShoppingRecipeIntegrationComponent | Yes | Form-Full |
| `/shopping/:id/bulk-edit` | ShoppingBulkEditorComponent | Yes | Form-Full |
| `/shopping/:id/share` | ShoppingListShareComponent | Yes | Form-Card |
| `/shopping/:id/export` | ShoppingListExportComponent | Yes | Form-Card |
| `/meal-plan` | MealPlanDashboardComponent | Yes | Dashboard |
| `/meal-plan/create` | MealPlanCreateComponent | Yes | Form-Card |
| `/meal-plan/calendar` | MealPlanCalendarComponent | Yes | Calendar |
| `/meal-plan/rules` | MealPlanRulesComponent | Yes | Settings |
| `/meal-plan/recipe-selection` | MealPlanRecipeSelectionComponent | Yes | Search |
| `/meal-plan/nutrition` | MealPlanNutritionComponent | Yes | Detail |
| `/meal-plan/print` | MealPlanPrintComponent | Yes | Detail |
| `/meal-plan/shopping-list` | MealPlanToShoppingListComponent | Yes | Form-Card |
| `/meal-plan/:id` | MealPlanDetailComponent | Yes | Detail |
| `/meal-plan/:id/edit` | MealPlanEditComponent | Yes | Form-Card |
| `/meal-plan/:id/shopping-list` | MealPlanToShoppingListComponent | Yes | Form-Card |
| `/meal-plan/:id/print` | MealPlanPrintComponent | Yes | Detail |
| `/meal-plan/:id/nutrition` | MealPlanNutritionComponent | Yes | Detail |
| `/messaging` | MessagingInboxComponent | Yes | Dashboard |
| `/messaging/new` | MessageComposeComponent | Yes | Form-Card |
| `/messaging/thread/:id` | MessageThreadDetailComponent | Yes | Detail |
| `/curation` | CurationQueueComponent | Yes (Admin) | Dashboard |
| `/admin/user-management` | UserManagementComponent | Yes (Admin) | Dashboard |
| `/user/dashboard` | RecipeAuthorDashboardComponent | Yes | Dashboard |
| `/user/privacy-settings` | PrivacySettingsComponent | Yes | Settings |
| `/privacy-settings` | PrivacySettingsComponent | Yes | Settings |
| `/edit-profile` | PersonProfileEditComponent | Yes | Form-Card |
| `/update-info` | UpdateInfoComponent | Yes | Form-Card |
| `/update-two-factor` | UpdateTwoFactorComponent | Yes | Form-Card |
| `/cookbook` | CookbookDashboardComponent | Yes | Dashboard |
| `/cookbook/create` | CookbookCreateComponent | Yes | Form-Card |
| `/cookbook/:id` | CookbookDetailComponent | Yes | Detail |
| `/cookbook/:id/edit` | CookbookEditComponent | Yes | Form-Card |
| `/curated-plans` | CuratedPlansComponent | Yes | Search |
| `/ingredient-search` | IngredientSearchComponent | Yes | Search |
| `/webhook` | WebhookDashboardComponent | Yes | Dashboard |
| `/labels` | LabelDashboardComponent | Yes | Dashboard |

## Appendix B: Service Dependencies

| Service | Used By Stories |
|---------|----------------|
| `AuthService` | US-AUTH-001 through US-AUTH-008, US-HOME-001 |
| `RecipeService` | US-RCP-001 through US-RCP-010, US-MP-007 |
| `RecipeSearchService` | US-RCP-006, US-HOME-003 |
| `CurationService` | US-CUR-001 through US-CUR-004, US-RCP-007 |
| `HouseholdService` | US-HH-001 through US-HH-007, US-SHELL-014 |
| `ShoppingListService` | US-SHOP-001 through US-SHOP-009, US-SHELL-013 |
| `MealPlanService` | US-MP-001 through US-MP-010, US-SHELL-012 |
| `MessagingService` | US-MSG-001 through US-MSG-004 |
| `PersonService` | US-PROF-001, US-SHELL-016, US-MSG-002 |
| `PrivacyService` | US-PRV-001 through US-PRV-004 |
| `UserManagementService` | US-ADM-001 |
| `CookbookService` | US-CB-001 through US-CB-004 |
| `PlanService` | US-CP-001 |
| `ThemeService` | US-SHELL-002 |
| `WebhookService` | US-WH-001 |
| `LabelService` | US-LBL-001 |
| `MeasurementService` | US-MSR-001 |
| `OnboardingService` | US-ONB-001 through US-ONB-005 |
