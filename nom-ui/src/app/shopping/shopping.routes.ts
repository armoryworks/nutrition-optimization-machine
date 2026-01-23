import { Routes } from "@angular/router";
import { AuthGuard } from "../guards/auth.guard";
import { ShoppingDashboardComponent } from "./components/shopping-dashboard/shopping-dashboard.component";

export const SHOPPING_ROUTES: Routes = [
    {
        path: "",
        component: ShoppingDashboardComponent,
        title: "Shopping Lists",
        canActivate: [AuthGuard],
    },
    {
        path: "create",
        loadComponent: () => import("./components/shopping-create/shopping-create.component").then(m => m.ShoppingCreateComponent),
        title: "Create Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: "categories",
        loadComponent: () => import("./components/shopping-category-management/shopping-category-management.component").then(m => m.ShoppingCategoryManagementComponent),
        title: "Shopping Categories",
        canActivate: [AuthGuard],
    },
    {
        path: ":id",
        loadComponent: () => import("./components/shopping-detail/shopping-detail.component").then(m => m.ShoppingDetailComponent),
        title: "Shopping List Details",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/edit",
        loadComponent: () => import("./components/shopping-edit/shopping-edit.component").then(m => m.ShoppingEditComponent),
        title: "Edit Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/recipes",
        loadComponent: () => import("./components/shopping-recipe-integration/shopping-recipe-integration.component").then(m => m.ShoppingRecipeIntegrationComponent),
        title: "Add Recipe to Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/bulk-edit",
        loadComponent: () => import("./components/shopping-bulk-editor/shopping-bulk-editor.component").then(m => m.ShoppingBulkEditorComponent),
        title: "Bulk Edit Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/share",
        loadComponent: () => import("./components/shopping-list-share/shopping-list-share.component").then(m => m.ShoppingListShareComponent),
        title: "Share Shopping List",
        canActivate: [AuthGuard],
    },
    {
        path: ":id/export",
        loadComponent: () => import("./components/shopping-list-export/shopping-list-export.component").then(m => m.ShoppingListExportComponent),
        title: "Export Shopping List",
        canActivate: [AuthGuard],
    },
]; 