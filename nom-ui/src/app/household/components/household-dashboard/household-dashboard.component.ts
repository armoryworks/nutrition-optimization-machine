import { Component, OnInit, inject, signal, ViewEncapsulation } from "@angular/core";

import { Router } from "@angular/router";

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent } from "angular-material-wrap";

import { HouseholdService } from "../../services/household.service";
import { HouseholdResponseModel } from "../../models/household-response.model";
import { ERROR_MESSAGES } from "../../../shared/constants/error-messages";

@Component({
    selector: "nom-household-dashboard",
    standalone: true,
    imports: [
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
    ],
    templateUrl: "./household-dashboard.component.html",
    styleUrls: ["./household-dashboard.component.scss"],
    encapsulation: ViewEncapsulation.None,
})
export class HouseholdDashboardComponent implements OnInit {
    private householdService = inject(HouseholdService);
    private router = inject(Router);

    households = signal<HouseholdResponseModel[]>([]);
    loading = signal(false);
    error = signal("");

    pageTitle = "Households";
    pageSubtitle = "Manage your household groups and coordinate with family members";



    ngOnInit(): void {
        this.loadHouseholds();
    }

    loadHouseholds(): void {
        this.loading.set(true);
        this.error.set("");

        this.householdService.getHouseholds().subscribe({
            next: (households) => {
                this.households.set(households);
                this.loading.set(false);
            },
            error: (error) => {
                this.error.set(ERROR_MESSAGES.HOUSEHOLD.LOAD_FAILED);
                this.loading.set(false);
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