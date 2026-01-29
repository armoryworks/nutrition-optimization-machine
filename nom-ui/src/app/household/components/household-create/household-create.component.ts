import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwIconComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdCreateRequestModel } from '../../models/household-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-household-create',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective
],
    templateUrl: './household-create.component.html',
    styleUrls: ['./household-create.component.scss']
})
export class HouseholdCreateComponent implements OnInit, OnDestroy {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private householdService = inject(HouseholdService);
    private router = inject(Router);
    private notificationService = inject(NotificationService);
    private userInfoService = inject(UserInfoService);
    private validationService = inject(AmwValidationService);

    householdForm: FormGroup = this.nonNullableFb.group({
        name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
        description: ['', [Validators.maxLength(500)]]
    });

    isLoading = signal(false);
    validationContext!: ValidationContext;

    formConfig = {
        title: 'Create Household',
        subtitle: 'Create a new household group to coordinate with family members',
        submitText: 'Create Household',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px',
    };

    constructor() {
        // Form is now initialized at declaration
    }

    ngOnInit(): void {
        // No need to set AuthorId - it will be handled by the backend

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

    onSubmit(): void {
        if (this.householdForm.valid) {
            this.isLoading.set(true);

            const createRequest = new HouseholdCreateRequestModel({
                name: this.householdForm.value.name,
                description: this.householdForm.value.description,
                groupId: 3 // Temporary: Using Recipe Type group ID (3) to fix foreign key constraint
            });

            this.householdService.createHousehold(createRequest)
                .pipe(loading('Creating household...'))
                .subscribe({
                    next: (response) => {
                        this.isLoading.set(false);
                        this.notificationService.success('Household created successfully!');
                        this.router.navigate(['/household', response.id]);
                    },
                    error: (error) => {
                        this.isLoading.set(false);
                        console.error('Error creating household:', error);
                        this.notificationService.error(ERROR_MESSAGES.HOUSEHOLD.SAVE_FAILED);
                    }
                });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household']);
    }
} 