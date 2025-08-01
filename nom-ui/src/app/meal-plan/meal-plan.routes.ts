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
        path: "rules",
        loadComponent: () => import("./components/meal-plan-rules/meal-plan-rules.component").then(m => m.MealPlanRulesComponent),
        title: "Meal Plan Rules",
        canActivate: [AuthGuard],
    },
]; 