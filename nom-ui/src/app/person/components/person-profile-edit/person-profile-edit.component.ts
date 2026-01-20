import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';
import {
    FormGroup,
    Validators,
    ReactiveFormsModule,
    NonNullableFormBuilder,
} from '@angular/forms';

// Angular Material Imports
import { AmwProgressBarComponent } from 'angular-material-wrap';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwInputComponent } from 'angular-material-wrap';

import { PersonService } from '../../services/person.service';
import { PersonModel } from '../../models/person.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-person-profile-edit',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwProgressBarComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwInputComponent,
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
    error = signal<string | null>(null);
    currentPerson: PersonModel | null = null;

    // Page configuration
    pageTitle = 'Edit Profile';
    pageSubtitle = 'Update your personal information';
    submitText = 'Save Changes';
    cancelText = 'Cancel';

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
        this.error.set(null);
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
            error: (err) => {
                this.isInitialLoading.set(false);
                console.error('Error loading person info:', err);
                this.error.set(err.message || 'Failed to load your profile information.');
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
        this.error.set(null);
        this.loadCurrentPerson();
    }
} 