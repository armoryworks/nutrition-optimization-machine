import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { WebhookService } from '../../services/webhook.service';
import { WebhookResponseModel, WebhookCreateRequestModel } from '../../models/webhook.models';
import { HouseholdService } from '../../../household/services/household.service';
import { HouseholdResponseModel } from '../../../household/models/household-response.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-webhook-dashboard',
    standalone: true,
    imports: [FormsModule, AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent],
    templateUrl: './webhook-dashboard.component.html',
    styleUrls: ['./webhook-dashboard.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class WebhookDashboardComponent implements OnInit {
    private webhookService = inject(WebhookService);
    private householdService = inject(HouseholdService);

    webhooks = signal<WebhookResponseModel[]>([]);
    households = signal<HouseholdResponseModel[]>([]);
    selectedHouseholdId = signal<number>(0);
    loading = signal(false);
    error = signal('');
    showCreateForm = signal(false);

    newWebhook: WebhookCreateRequestModel = { householdId: 0, name: '', url: '' };

    pageTitle = 'Webhooks';
    pageSubtitle = 'Configure event notifications for external services';

    ngOnInit(): void {
        this.householdService.getHouseholds().subscribe({
            next: (households) => {
                this.households.set(households);
                if (households.length > 0) {
                    this.selectedHouseholdId.set(households[0].id);
                    this.loadWebhooks();
                }
            },
            error: () => this.error.set(ERROR_MESSAGES.HOUSEHOLD.LOAD_FAILED),
        });
    }

    loadWebhooks(): void {
        const householdId = this.selectedHouseholdId();
        if (!householdId) return;

        this.loading.set(true);
        this.error.set('');

        this.webhookService.getWebhooks(householdId).subscribe({
            next: (webhooks) => { this.webhooks.set(webhooks); this.loading.set(false); },
            error: () => { this.error.set(ERROR_MESSAGES.WEBHOOK.LOAD_FAILED); this.loading.set(false); },
        });
    }

    onHouseholdChange(householdId: number): void {
        this.selectedHouseholdId.set(householdId);
        this.loadWebhooks();
    }

    toggleCreateForm(): void {
        this.showCreateForm.set(!this.showCreateForm());
        this.newWebhook = { householdId: this.selectedHouseholdId(), name: '', url: '' };
    }

    createWebhook(): void {
        this.newWebhook.householdId = this.selectedHouseholdId();
        this.webhookService.createWebhook(this.newWebhook).subscribe({
            next: () => { this.showCreateForm.set(false); this.loadWebhooks(); },
            error: () => this.error.set(ERROR_MESSAGES.WEBHOOK.SAVE_FAILED),
        });
    }

    testWebhook(webhook: WebhookResponseModel): void {
        this.webhookService.testWebhook(webhook.id).subscribe({
            next: (result) => { if (!result.success) this.error.set(ERROR_MESSAGES.WEBHOOK.TEST_FAILED); },
            error: () => this.error.set(ERROR_MESSAGES.WEBHOOK.TEST_FAILED),
        });
    }

    deleteWebhook(webhook: WebhookResponseModel): void {
        if (!confirm(`Delete webhook "${webhook.name}"?`)) return;
        this.webhookService.deleteWebhook(webhook.id).subscribe({
            next: () => this.loadWebhooks(),
            error: () => this.error.set(ERROR_MESSAGES.WEBHOOK.DELETE_FAILED),
        });
    }

    toggleActive(webhook: WebhookResponseModel): void {
        this.webhookService.updateWebhook(webhook.id, { isActive: !webhook.isActive }).subscribe({
            next: () => this.loadWebhooks(),
            error: () => this.error.set(ERROR_MESSAGES.WEBHOOK.SAVE_FAILED),
        });
    }
}
