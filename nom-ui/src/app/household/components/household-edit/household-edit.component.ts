import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel } from '../../models/household-response.model';
import { HouseholdUpdateRequestModel } from '../../models/household-update-request.model';
import { BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
    selector: 'nom-household-edit',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwProgressSpinnerComponent
],
    templateUrl: './household-edit.component.html',
    styleUrls: ['./household-edit.component.scss']
})
export class HouseholdEditComponent implements OnInit {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private householdService = inject(HouseholdService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private notificationService = inject(NotificationService);

    householdForm: FormGroup = this.nonNullableFb.group({
        name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
        description: ['', [Validators.maxLength(500)]]
    });

    isLoading = signal(false);
    householdId = signal(0);
    household = signal<HouseholdResponseModel | null>(null);

    formConfig: BaseFormConfig = {
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
                this.notificationService.error('Failed to load household details');
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

            this.householdService.updateHousehold(this.householdId(), updateRequest).subscribe({
                next: () => {
                    this.isLoading.set(false);
                    this.notificationService.success('Household updated successfully!');
                    this.router.navigate(['/household', this.householdId()]);
                },
                error: (error) => {
                    this.isLoading.set(false);
                    console.error('Error updating household:', error);
                    this.notificationService.error('Failed to update household. Please try again.');
                }
            });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household', this.householdId()]);
    }
} 