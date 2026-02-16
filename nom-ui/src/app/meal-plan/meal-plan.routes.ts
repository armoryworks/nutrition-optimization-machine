import { Routes } from "@angular/router";
import { AuthGuard } from "../guards/auth.guard";
import { MealPlanDashboardComponent } from "./components/meal-plan-dashboard/meal-plan-dashboard.component";

export const MEAL_PLAN_ROUTES: Routes = [
    {
        path: "",
        component: MealPlanDashboardComponent,
        title: "Meal Plans",
        canActivate: [AuthGuard],
    },
    {
        path: "create",
        loadComponent: () => import("./components/meal-plan-create/meal-plan-create.component").then(m => m.MealPlanCreateComponent),
        title: "Create Meal Plan",
        canActivate: [AuthGuard],
    },
    // Static routes MUST come before parameterized :id routes
    {
        path: "rules",
        loadComponent: () => import("./components/meal-plan-rules/meal-plan-rules.component").then(m => m.MealPlanRulesComponent),
        title: "Meal Plan Rules",
        canActivate: [AuthGuard],
    },
    {
        path: "calendar",
        loadComponent: () => import("./components/meal-plan-calendar/meal-plan-calendar.component").then(m => m.MealPlanCalendarComponent),
        title: "Meal Plan Calendar",
        canActivate: [AuthGuard],
    },
    {
        path: "recipe-selection",
        loadComponent: () => import("./components/meal-plan-recipe-selection/meal-plan-recipe-selection.component").then(m => m.MealPlanRecipeSelectionComponent),
        title: "Add Recipe to Meal Plan",
        canActivate: [AuthGuard],
    },
    {
        path: "shopping-list",
        loadComponent: () => import("./components/meal-plan-to-shopping-list/meal-plan-to-shopping-list.component").then(m => m.MealPlanToShoppingListComponent),
        title: "Generate Shopping List from Week",
        canActivate: [AuthGuard],
    },
    {
        path: "print",
        loadComponent: () => import("./components/meal-plan-print/meal-plan-print.component").then(m => m.MealPlanPrintComponent),
        title: "Print Weekly Meal Plan",
        canActivate: [AuthGuard],
    },
    {
        path: "nutrition",
        loadComponent: () => import("./components/meal-plan-nutrition/meal-plan-nutrition.component").then(m => m.MealPlanNutritionComponent),
        title: "Weekly Nutrition",
        canActivate: [AuthGuard],
    },
    // Parameterized routes AFTER all static routes
    {
        path: ":id",
        loadComponent: () => import("./components/meal-plan-detail/meal-plan-detail.component").then(m => m.MealPlanDetailComponent),
        title: "Meal Plan Details",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/edit",
        loadComponent: () => import("./components/meal-plan-edit/meal-plan-edit.component").then(m => m.MealPlanEditComponent),
        title: "Edit Meal Plan",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/shopping-list",
        loadComponent: () => import("./components/meal-plan-to-shopping-list/meal-plan-to-shopping-list.component").then(m => m.MealPlanToShoppingListComponent),
        title: "Generate Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/print",
        loadComponent: () => import("./components/meal-plan-print/meal-plan-print.component").then(m => m.MealPlanPrintComponent),
        title: "Print Meal Plan",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/nutrition",
        loadComponent: () => import("./components/meal-plan-nutrition/meal-plan-nutrition.component").then(m => m.MealPlanNutritionComponent),
        title: "Nutrition Information",
        canActivate: [AuthGuard],
    },
];
