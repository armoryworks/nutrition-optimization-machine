import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel, HouseholdUpdateRequestModel } from '../../models/household.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
    selector: 'app-household-edit',
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
    templateUrl: './household-edit.component.html',
    styleUrls: ['./household-edit.component.scss']
})
export class HouseholdEditComponent implements OnInit {
    householdForm: FormGroup;
    isLoading = false;
    householdId: number = 0;
    household: HouseholdResponseModel | null = null;

    formConfig: BaseFormConfig = {
        title: 'Edit Household',
        subtitle: 'Update your household information',
        submitText: 'Update Household',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px',
    };

    constructor(
        private formBuilder: FormBuilder,
        private householdService: HouseholdService,
        private route: ActivatedRoute,
        private router: Router,
        private snackBar: MatSnackBar
    ) {
        this.householdForm = this.formBuilder.group({
            Name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
            Description: ['', [Validators.maxLength(500)]]
        });
    }

    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId = +params['id'];
            this.loadHousehold();
        });
    }

    loadHousehold(): void {
        this.isLoading = true;

        this.householdService.getHousehold(this.householdId).subscribe({
            next: (household) => {
                this.household = household;
                this.householdForm.patchValue({
                    Name: household.name,
                    Description: household.description || ''
                });
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading household:', error);
                this.snackBar.open('Failed to load household details', 'Close', {
                    duration: 5000,
                    horizontalPosition: 'center',
                    verticalPosition: 'top'
                });
                this.router.navigate(['/household']);
            }
        });
    }

    onSubmit(): void {
        if (this.householdForm.valid && this.household) {
            this.isLoading = true;

            const updateRequest = new HouseholdUpdateRequestModel({
                name: this.householdForm.value.Name,
                description: this.householdForm.value.Description
            });

            this.householdService.updateHousehold(this.householdId, updateRequest).subscribe({
                next: (response) => {
                    this.isLoading = false;
                    this.snackBar.open('Household updated successfully!', 'Close', {
                        duration: 3000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                    this.router.navigate(['/household', this.householdId]);
                },
                error: (error) => {
                    this.isLoading = false;
                    console.error('Error updating household:', error);
                    this.snackBar.open('Failed to update household. Please try again.', 'Close', {
                        duration: 5000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                }
            });
        }
    }

    onCancel(): void {
        this.router.navigate(['/household', this.householdId]);
    }
} 