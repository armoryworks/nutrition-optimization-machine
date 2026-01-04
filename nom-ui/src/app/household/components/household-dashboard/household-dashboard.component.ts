import { Component, OnInit, inject } from "@angular/core";

import { ReactiveFormsModule, NonNullableFormBuilder } from "@angular/forms";
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatIconModule } from "@angular/material/icon";
import { MatButtonModule } from "@angular/material/button";
import { MatTableModule } from "@angular/material/table";
import { MatPaginatorModule } from "@angular/material/paginator";
import { MatSortModule } from "@angular/material/sort";
import { MatChipsModule } from "@angular/material/chips";
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { Router } from "@angular/router";
import { HouseholdService } from "../../services/household.service";
import { HouseholdResponseModel } from "../../models/household-response.model";
import { ViewEncapsulation } from "@angular/core";
import { BasePageComponent, BasePageConfig } from "../../../common/components/base-page/base-page.component";

@Component({
    selector: "nom-household-dashboard",
    standalone: true,
    imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatChipsModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    BasePageComponent
],
    templateUrl: "./household-dashboard.component.html",
    styleUrls: ["./household-dashboard.component.scss"],
    encapsulation: ViewEncapsulation.None,
})
export class HouseholdDashboardComponent implements OnInit {
    private householdService = inject(HouseholdService);
    private router = inject(Router);
    private fb = inject(NonNullableFormBuilder);

    households: HouseholdResponseModel[] = [];
    loading = false;
    error = "";

    pageConfig: BasePageConfig = {
        title: "Households",
        subtitle: "Manage your household groups and coordinate with family members",
        showRefreshButton: true,
        refreshButtonText: "Refresh",
        maxWidth: "1200px",
    };



    ngOnInit(): void {
        this.loadHouseholds();
    }

    loadHouseholds(): void {
        this.loading = true;
        this.error = "";

        this.householdService.getHouseholds().subscribe({
            next: (households) => {
                this.households = households;
                this.loading = false;
            },
            error: (error) => {
                this.error = "Failed to load households";
                this.loading = false;
                console.error("Error loading households:", error);
            },
        });
    }

    onRefresh(): void {
        this.loadHouseholds();
    }

    onRetry(): void {
        this.loadHouseholds();
    }

    createHousehold(): void {
        this.router.navigate(["/household/create"]);
    }

    viewHousehold(household: HouseholdResponseModel): void {
        if (household.id) {
            this.router.navigate(["/household", household.id]);
        }
    }

    editHousehold(household: HouseholdResponseModel): void {
        if (household.id) {
            this.router.navigate(["/household", household.id, "edit"]);
        }
    }

    inviteMembers(household: HouseholdResponseModel): void {
        if (household.id) {
            this.router.navigate(["/household", household.id, "invite"]);
        }
    }
} 