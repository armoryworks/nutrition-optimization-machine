import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwProgressSpinnerComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel } from '../../models/household-response.model';
import { HouseholdUpdateRequestModel } from '../../models/household-update-request.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-household-edit',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwProgressSpinnerComponent,
    AmwValidationTooltipDirective
],
    templateUrl: './household-edit.component.html',
    styleUrls: ['./household-edit.component.scss']
})
export class HouseholdEditComponent implements OnInit, OnDestroy {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private householdService = inject(HouseholdService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private notificationService = inject(NotificationService);
    private validationService = inject(AmwValidationService);

    householdForm: FormGroup = this.nonNullableFb.group({
        name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
        description: ['', [Validators.maxLength(500)]]
    });

    isLoading = signal(false);
    householdId = signal(0);
    household = signal<HouseholdResponseModel | null>(null);
    validationContext!: ValidationContext;

    formConfig = {
        title: 'Edit Household',
        subtitle: 'Update your household information',
        submitText: 'Update Household',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px',
    };

    constructor() {
        // Form is now initialized at declaration
    }

    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId.set(+params['id']);
            this.loadHousehold();
        });

        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Name validations
        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-required',
            message: 'Household name is required',
            severity: 'error',
            field: 'name',
            control: this.householdForm.get('name') ?? undefined,
            validator: () => !this.householdForm.get('name')?.hasError('required')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-minlength',
            message: 'Household name must be at least 2 characters',
            severity: 'error',
            field: 'name',
            control: this.householdForm.get('name') ?? undefined,
            validator: () => !this.householdForm.get('name')?.hasError('minlength')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-maxlength',
            message: 'Household name cannot exceed 100 characters',
            severity: 'error',
            field: 'name',
            control: this.householdForm.get('name') ?? undefined,
            validator: () => !this.householdForm.get('name')?.hasError('maxlength')
        });

        // Description validation (optional field)
        this.validationService.addViolation(this.validationContext.id, {
            id: 'description-maxlength',
            message: 'Description cannot exceed 500 characters',
            severity: 'error',
            field: 'description',
            control: this.householdForm.get('description') ?? undefined,
            validator: () => !this.householdForm.get('description')?.hasError('maxlength')
        });
    }

    ngOnDestroy(): void {
        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    loadHousehold(): void {
        this.isLoading.set(true);

        this.householdService.getHousehold(this.householdId()).subscribe({
            next: (household) => {
                this.household.set(household);
                this.householdForm.patchValue({
                    name: household.name,
                    description: household.description || ''
                });
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading household:', error);
                this.notificationService.error(ERROR_MESSAGES.HOUSEHOLD.LOAD_FAILED);
                this.router.navigate(['/household']);
            }
        });
    }

    onSubmit(): void {
        if (this.householdForm.valid && this.household()) {
            this.isLoading.set(true);

            const updateRequest = new HouseholdUpdateRequestModel({
                name: this.householdForm.value.name,
                description: this.householdForm.value.description
            });

            this.householdService.updateHousehold(this.householdId(), updateRequest)
                .pipe(loading('Updating household...'))
                .subscribe({
                    next: () => {
                        this.isLoading.set(false);
                        this.notificationService.success('Household updated successfully!');
                        this.router.navigate(['/household', this.householdId()]);
                    },
                    error: (error) => {
                        this.isLoading.set(false);
                        console.error('Error updating household:', error);
                        this.notificationService.error(ERROR_MESSAGES.HOUSEHOLD.SAVE_FAILED);
                    }
                });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household', this.householdId()]);
    }
} 