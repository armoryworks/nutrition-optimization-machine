import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { HouseholdService } from '../../services/household.service';
import { HouseholdCreateRequestModel } from '../../models/household-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
    selector: 'nom-household-create',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        BaseFormComponent,
    ],
    templateUrl: './household-create.component.html',
    styleUrls: ['./household-create.component.scss']
})
export class HouseholdCreateComponent implements OnInit {
    householdForm: FormGroup;
    isLoading = false;

    formConfig: BaseFormConfig = {
        title: 'Create Household',
        subtitle: 'Create a new household group to coordinate with family members',
        submitText: 'Create Household',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px',
    };

    constructor(
        private nonNullableFb: NonNullableFormBuilder,
        private householdService: HouseholdService,
        private router: Router,
        private snackBar: MatSnackBar,
        private userInfoService: UserInfoService
    ) {
        this.householdForm = this.nonNullableFb.group({
            Name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
            Description: ['', [Validators.maxLength(500)]],
            GroupId: [null],
            AuthorId: [null]
        });
    }

    ngOnInit(): void {
        // Set default values or get from current user context
        const currentPersonId = this.userInfoService.getCurrentUserInfoValue()?.personId;
        this.householdForm.patchValue({
            AuthorId: currentPersonId || 1 // Use current person ID or fallback
        });
    }

    onSubmit(): void {
        if (this.householdForm.valid) {
            this.isLoading = true;

            const createRequest = new HouseholdCreateRequestModel({
                name: this.householdForm.value.Name,
                description: this.householdForm.value.Description
            });

            this.householdService.createHousehold(createRequest).subscribe({
                next: (response) => {
                    this.isLoading = false;
                    this.snackBar.open('Household created successfully!', 'Close', {
                        duration: 3000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                    this.router.navigate(['/household', response.id]);
                },
                error: (error) => {
                    this.isLoading = false;
                    console.error('Error creating household:', error);
                    this.snackBar.open('Failed to create household. Please try again.', 'Close', {
                        duration: 5000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                }
            });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household']);
    }
} 