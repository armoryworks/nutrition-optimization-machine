import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { CookbookService } from '../../services/cookbook.service';
import { CookbookResponseModel } from '../../models/cookbook-response.model';
import { HouseholdService } from '../../../household/services/household.service';
import { HouseholdResponseModel } from '../../../household/models/household-response.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-cookbook-dashboard',
    standalone: true,
    imports: [
        FormsModule,
        AmwButtonComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
    ],
    templateUrl: './cookbook-dashboard.component.html',
    styleUrls: ['./cookbook-dashboard.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class CookbookDashboardComponent implements OnInit {
    private cookbookService = inject(CookbookService);
    private householdService = inject(HouseholdService);
    private router = inject(Router);

    cookbooks = signal<CookbookResponseModel[]>([]);
    households = signal<HouseholdResponseModel[]>([]);
    selectedHouseholdId = signal<number>(0);
    loading = signal(false);
    error = signal('');

    pageTitle = 'Cookbooks';
    pageSubtitle = 'Organize your recipes into themed collections';

    ngOnInit(): void {
        this.loadHouseholds();
    }

    loadHouseholds(): void {
        this.householdService.getHouseholds().subscribe({
            next: (households) => {
                this.households.set(households);
                if (households.length > 0) {
                    this.selectedHouseholdId.set(households[0].id);
                    this.loadCookbooks();
                }
            },
            error: () => {
                this.error.set(ERROR_MESSAGES.HOUSEHOLD.LOAD_FAILED);
            },
        });
    }

    loadCookbooks(): void {
        const householdId = this.selectedHouseholdId();
        if (!householdId) return;

        this.loading.set(true);
        this.error.set('');

        this.cookbookService.getCookbooks(householdId).subscribe({
            next: (cookbooks) => {
                this.cookbooks.set(cookbooks);
                this.loading.set(false);
            },
            error: () => {
                this.error.set(ERROR_MESSAGES.COOKBOOK?.LOAD_FAILED ?? 'Failed to load cookbooks. Please try again.');
                this.loading.set(false);
            },
        });
    }

    onHouseholdChange(householdId: number): void {
        this.selectedHouseholdId.set(householdId);
        this.loadCookbooks();
    }

    onRefresh(): void {
        this.loadCookbooks();
    }

    onRetry(): void {
        this.loadCookbooks();
    }

    createCookbook(): void {
        this.router.navigate(['/cookbook/create'], {
            queryParams: { householdId: this.selectedHouseholdId() }
        });
    }

    viewCookbook(cookbook: CookbookResponseModel): void {
        this.router.navigate(['/cookbook', cookbook.id]);
    }

    editCookbook(cookbook: CookbookResponseModel): void {
        this.router.navigate(['/cookbook', cookbook.id, 'edit']);
    }

    deleteCookbook(cookbook: CookbookResponseModel): void {
        if (!confirm(`Delete cookbook "${cookbook.name}"?`)) return;

        this.cookbookService.deleteCookbook(cookbook.id).subscribe({
            next: () => this.loadCookbooks(),
            error: () => this.error.set(ERROR_MESSAGES.COOKBOOK?.DELETE_FAILED ?? 'Failed to delete cookbook.'),
        });
    }
}
