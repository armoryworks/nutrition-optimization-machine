import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwDividerComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { HouseholdService } from '../../services/household.service';
import { HouseholdInviteTokenCreateRequestModel } from '../../models/household-invite-token-create-request.model';
import { HouseholdInviteTokenResponseModel } from '../../models/household-invite-token-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-household-invite-refactored',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwInputComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwDividerComponent,
        AmwValidationTooltipDirective,
    ],
    templateUrl: './household-invite-refactored.component.html',
    styleUrls: ['./household-invite-refactored.component.scss']
})
export class HouseholdInviteRefactoredComponent implements OnInit, OnDestroy {
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private householdService = inject(HouseholdService);
    private fb = inject(FormBuilder);
    private notificationService = inject(NotificationService);
    private validationService = inject(AmwValidationService);

    inviteForm: FormGroup;
    householdId = signal(0);
    isLoading = signal(false);
    inviteToken = signal<string | null>(null);
    inviteLink = signal<string | null>(null);
    error = signal<string | null>(null);
    validationContext!: ValidationContext;

    pageTitle = 'Invite Members to Household';
    pageSubtitle = 'Generate an invite link to share with others';

    constructor() {
        this.inviteForm = this.fb.group({
            expiresInDays: [7, [Validators.required, Validators.min(1), Validators.max(30)]]
        });
    }

    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId.set(+params['id']);
        });

        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Expiration days validations
        this.validationService.addViolation(this.validationContext.id, {
            id: 'expiresInDays-required',
            message: 'Expiration days is required',
            severity: 'error',
            field: 'expiresInDays',
            control: this.inviteForm.get('expiresInDays') ?? undefined,
            validator: () => !this.inviteForm.get('expiresInDays')?.hasError('required')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'expiresInDays-min',
            message: 'Expiration must be at least 1 day',
            severity: 'error',
            field: 'expiresInDays',
            control: this.inviteForm.get('expiresInDays') ?? undefined,
            validator: () => !this.inviteForm.get('expiresInDays')?.hasError('min')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'expiresInDays-max',
            message: 'Expiration cannot exceed 30 days',
            severity: 'error',
            field: 'expiresInDays',
            control: this.inviteForm.get('expiresInDays') ?? undefined,
            validator: () => !this.inviteForm.get('expiresInDays')?.hasError('max')
        });
    }

    ngOnDestroy(): void {
        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    generateInviteToken(): void {
        if (this.inviteForm.valid) {
            this.isLoading.set(true);
            this.error.set(null);

            const request: HouseholdInviteTokenCreateRequestModel = {
                householdId: this.householdId(),
                expiresAt: new Date(Date.now() + this.inviteForm.value.expiresInDays * 24 * 60 * 60 * 1000)
            };

            this.householdService.createInviteToken(request).subscribe({
                next: (response: HouseholdInviteTokenResponseModel) => {
                    this.inviteToken.set(response.token);
                    this.inviteLink.set(`${window.location.origin}/household/join?token=${response.token}`);
                    this.isLoading.set(false);
                    this.notificationService.success('Invite token generated successfully');
                },
                error: (error) => {
                    console.error('Error generating invite token:', error);
                    this.error.set(ERROR_MESSAGES.HOUSEHOLD.INVITE_FAILED);
                    this.isLoading.set(false);
                    this.notificationService.error(ERROR_MESSAGES.HOUSEHOLD.INVITE_FAILED);
                }
            });
        }
    }

    copyInviteLink(): void {
        if (this.inviteLink()) {
            navigator.clipboard.writeText(this.inviteLink()!).then(() => {
                this.notificationService.success('Invite link copied to clipboard');
            }).catch(() => {
                this.notificationService.error(ERROR_MESSAGES.CLIPBOARD.COPY_FAILED);
            });
        }
    }

    copyToken(): void {
        if (this.inviteToken()) {
            navigator.clipboard.writeText(this.inviteToken()!).then(() => {
                this.notificationService.success('Invite token copied to clipboard');
            }).catch(() => {
                this.notificationService.error(ERROR_MESSAGES.CLIPBOARD.COPY_FAILED);
            });
        }
    }

    onBack(): void {
        this.router.navigate(['/household', this.householdId()]);
    }
} 