// File: nom-ui/src/app/admin/components/user-management/user-management.component.ts

import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { Router } from '@angular/router';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import {
  AmwButtonComponent,
  AmwCardComponent,
  AmwIconComponent,
  AmwInlineLoadingComponent,
  AmwInputComponent,
  AmwDialogService,
} from 'angular-material-wrap';

import { PersonService } from '../../../person/services/person.service';
import { PersonModel } from '../../../person/models/person.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-user-management',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwInlineLoadingComponent,
    AmwInputComponent,
  ],
  templateUrl: './user-management.component.html',
  styleUrls: ['./user-management.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class UserManagementComponent implements OnInit {
  private personService = inject(PersonService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(AmwDialogService);
  private router = inject(Router);

  persons = signal<PersonModel[]>([]);
  filteredPersons = signal<PersonModel[]>([]);
  isLoading = signal(true);
  error = signal<string | null>(null);
  searchControl = new FormControl('');

  pageTitle = 'User Management';
  pageSubtitle = 'Manage users and their information';

  ngOnInit(): void {
    this.loadPersons();
    this.setupSearchFilter();
  }

  setupSearchFilter(): void {
    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged())
      .subscribe((searchTerm) => {
        this.filterPersons(searchTerm || '');
      });
  }

  loadPersons(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.personService.getAllPersons().subscribe({
      next: (persons) => {
        this.persons.set(persons);
        this.filteredPersons.set([...this.persons()]);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading persons:', error);
        this.error.set(ERROR_MESSAGES.ADMIN.LOAD_USERS_FAILED);
        this.isLoading.set(false);
      },
    });
  }

  filterPersons(searchTerm: string): void {
    if (!searchTerm.trim()) {
      this.filteredPersons.set([...this.persons()]);
    } else {
      const term = searchTerm.toLowerCase();
      this.filteredPersons.set(
        this.persons().filter(
          (person) =>
            person.name.toLowerCase().includes(term) ||
            person.id?.toString().includes(term)
        )
      );
    }
  }

  onCreateUser(): void {
    this.router.navigate(['/admin/user-management/create']);
  }

  onViewUser(personId: number | undefined): void {
    if (personId) {
      this.router.navigate(['/admin/user-management', personId]);
    }
  }

  onEditUser(personId: number | undefined): void {
    if (personId) {
      this.router.navigate(['/admin/user-management', personId, 'edit']);
    }
  }

  onDeleteUser(person: PersonModel): void {
    if (!person.id) {
      return;
    }

    this.dialogService
      .confirm(
        `Are you sure you want to delete user "${person.name}"? This action cannot be undone.`,
        'Delete User'
      )
      .subscribe((result) => {
        if (result && person.id) {
          this.personService.deletePerson(person.id).subscribe({
            next: () => {
              this.notificationService.success('User deleted successfully');
              this.loadPersons();
            },
            error: (error) => {
              console.error('Error deleting user:', error);
              this.notificationService.error(ERROR_MESSAGES.ADMIN.DELETE_USER_FAILED);
            },
          });
        }
      });
  }

  onRefresh(): void {
    this.loadPersons();
  }

  onRetry(): void {
    this.loadPersons();
  }

  getPlanCount(person: PersonModel): number {
    // Placeholder - this would be extended if PersonModel includes plan information
    return 0;
  }

  getAttributeCount(person: PersonModel): number {
    return person.attributes?.length || 0;
  }

  getUserIdDisplay(person: PersonModel): string {
    return person.id ? `ID: ${person.id}` : 'No ID';
  }
}
