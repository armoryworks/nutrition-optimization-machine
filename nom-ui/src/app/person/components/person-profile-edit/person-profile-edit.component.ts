import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';
import {
    FormGroup,
    Validators,
    ReactiveFormsModule,
    NonNullableFormBuilder,
} from '@angular/forms';


// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { PersonService } from '../../services/person.service';
import { PersonModel } from '../../models/person.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'nom-person-profile-edit',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    BaseFormComponent,
    BasePageComponent
],
    templateUrl: './person-profile-edit.component.html',
    styleUrls: ['./person-profile-edit.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class PersonProfileEditComponent implements OnInit {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private personService = inject(PersonService);
    private notificationService = inject(NotificationService);

    personForm: FormGroup;
    isLoading = signal(false);
    isInitialLoading = signal(true);
    currentPerson: PersonModel | null = null;

    pageConfig: BasePageConfig = {
        title: 'Edit Profile',
        subtitle: 'Update your personal information',
        showBackButton: true,
        maxWidth: '600px'
    };

    formConfig: BaseFormConfig = {
        title: '',
        subtitle: '',
        submitText: 'Save Changes',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '100%'
    };

    constructor() {
        // Initialize the form group
        this.personForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2)]],
        });
    }

    ngOnInit(): void {
        this.loadCurrentPerson();
    }

    private loadCurrentPerson(): void {
        this.isInitialLoading.set(true);
        this.personService.getCurrentPerson().subscribe({
            next: (person) => {
                if (person) {
                    this.currentPerson = person;
                    this.personForm.patchValue({
                        name: person.name || '',
                    });
                }
                this.isInitialLoading.set(false);
            },
            error: (error) => {
                this.isInitialLoading.set(false);
                console.error('Error loading person info:', error);
                this.notificationService.error(
                    error.message || 'Failed to load your profile information.'
                );
            },
        });
    }

    onSubmit(): void {
        if (this.personForm.invalid) {
            this.notificationService.error('Please provide a valid name.');
            return;
        }

        this.isLoading.set(true);
        const personName = this.personForm.get('name')?.value;

        this.personService
            .upsertPerson({ personName })
            .subscribe({
                next: (response) => {
                    this.isLoading.set(false);
                    this.currentPerson = new PersonModel({
                        id: response.id,
                        name: response.name
                    });
                    this.notificationService.success('Profile updated successfully!');
                },
                error: (error) => {
                    this.isLoading.set(false);
                    console.error('Error updating person:', error);
                    this.notificationService.error(
                        error.error?.message || error.message || 'Failed to update profile.'
                    );
                },
            });
    }

    onCancel(): void {
        // Reset form to original values
        if (this.currentPerson) {
            this.personForm.patchValue({
                name: this.currentPerson.name || '',
            });
        }
    }

    onBack(): void {
        // Handle back navigation
        window.history.back();
    }

    onRefresh(): void {
        this.loadCurrentPerson();
    }

    onRetry(): void {
        this.loadCurrentPerson();
    }
} 