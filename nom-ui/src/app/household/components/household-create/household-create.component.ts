import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdCreateRequestModel } from '../../models/household-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
    selector: 'nom-household-create',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent
],
    templateUrl: './household-create.component.html',
    styleUrls: ['./household-create.component.scss']
})
export class HouseholdCreateComponent implements OnInit {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private householdService = inject(HouseholdService);
    private router = inject(Router);
    private notificationService = inject(NotificationService);
    private userInfoService = inject(UserInfoService);

    householdForm: FormGroup = this.nonNullableFb.group({
        name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
        description: ['', [Validators.maxLength(500)]]
    });

    isLoading = signal(false);

    formConfig: BaseFormConfig = {
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
    }

    onSubmit(): void {
        if (this.householdForm.valid) {
            this.isLoading.set(true);

            const createRequest = new HouseholdCreateRequestModel({
                name: this.householdForm.value.name,
                description: this.householdForm.value.description,
                groupId: 3 // Temporary: Using Recipe Type group ID (3) to fix foreign key constraint
            });

            this.householdService.createHousehold(createRequest).subscribe({
                next: (response) => {
                    this.isLoading.set(false);
                    this.notificationService.success('Household created successfully!');
                    this.router.navigate(['/household', response.id]);
                },
                error: (error) => {
                    this.isLoading.set(false);
                    console.error('Error creating household:', error);
                    this.notificationService.error('Failed to create household. Please try again.');
                }
            });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household']);
    }
} 