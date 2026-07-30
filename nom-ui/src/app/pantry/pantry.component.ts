import {
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
  OnInit,
  ChangeDetectionStrategy,
} from '@angular/core';
import { toLocalDateString } from '../core/utils/local-date';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
// RouterLink not used directly but may be needed for future navigation
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { PantryService } from '../core/services/pantry.service';
import { HouseholdService } from '../core/services/household.service';
import { MeasurementService } from '../core/services/measurement.service';
import { IngredientService } from '../core/services/ingredient.service';
import { PantryItemResponse } from '../core/models/pantry-item-response.model';
import { PantryItemCreateRequest } from '../core/models/pantry-item-create-request.model';
import { IngredientSearchResult } from '../core/models/ingredient-search-result.model';
import { MeasurementOption } from '../core/models/measurement.model';
import { debounceTime, distinctUntilChanged, Subject, switchMap, of } from 'rxjs';
import { categorizeDepartment } from '../core/domain/shopping/departments';
import { shelfLifeDaysFor } from '../core/domain/shopping/shelf-life';
import { formatQuantity } from '../core/domain/shopping/unit-conversion';

@Component({
  selector: 'nom-pantry',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatAutocompleteModule,
  ],
  templateUrl: './pantry.component.html',
  styleUrls: ['./pantry.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PantryComponent implements OnInit {
  private pantryService = inject(PantryService);
  private householdService = inject(HouseholdService);
  private measurementService = inject(MeasurementService);
  private ingredientService = inject(IngredientService);

  loading = signal(true);
  error = signal<string | null>(null);
  items = signal<PantryItemResponse[]>([]);
  householdId = signal(0);
  showAddForm = signal(false);

  // Add form state
  ingredientSearch = signal('');
  ingredientOptions = signal<IngredientSearchResult[]>([]);
  selectedIngredient = signal<IngredientSearchResult | null>(null);
  newQuantity = signal<number>(1);
  measurements = signal<MeasurementOption[]>([]);
  selectedMeasurementId = signal<number | null>(null);
  adding = signal(false);

  private destroyRef = inject(DestroyRef);
  private searchSubject = new Subject<string>();

  // Computed views
  activeItems = computed(() =>
    this.items().filter((i) => !i.isExpired && i.statusName === 'In Pantry'),
  );

  expiredItems = computed(() => this.items().filter((i) => i.isExpired));

  expiringSoonItems = computed(() => this.activeItems().filter((i) => i.isExpiringSoon));

  ngOnInit() {
    this.loadHouseholdThenItems();
    this.loadMeasurements();
    this.setupIngredientSearch();
  }

  private loadHouseholdThenItems() {
    this.loading.set(true);
    this.error.set(null);

    this.householdService
      .getHouseholds()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          if (list.length > 0) {
            this.householdId.set(list[0].id);
            this.loadPantryItems();
          } else {
            this.error.set('No household found.');
            this.loading.set(false);
          }
        },
        error: () => {
          this.error.set('Failed to load household.');
          this.loading.set(false);
        },
      });
  }

  private loadPantryItems() {
    this.loading.set(true);
    this.error.set(null);

    this.pantryService
      .getPantryItems(this.householdId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.items.set(items);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Failed to load pantry items');
          this.loading.set(false);
        },
      });
  }

  private loadMeasurements() {
    this.measurementService
      .loadMeasurements()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (m) => this.measurements.set(m),
      });
  }

  private setupIngredientSearch() {
    this.searchSubject
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((query) => {
          if (query.length < 2) return of([]);
          return this.ingredientService.searchIngredients(query);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => this.ingredientOptions.set(options));
  }

  onIngredientSearchChange(value: string) {
    this.ingredientSearch.set(value);
    this.selectedIngredient.set(null);
    this.searchSubject.next(value);
  }

  selectIngredient(option: IngredientSearchResult) {
    this.selectedIngredient.set(option);
    this.ingredientSearch.set(option.name);
    this.ingredientOptions.set([]);
  }

  displayIngredient(option: IngredientSearchResult): string {
    return option?.name ?? '';
  }

  toggleAddForm() {
    this.showAddForm.update((v) => !v);
    if (!this.showAddForm()) {
      this.resetAddForm();
    }
  }

  addItem() {
    const ingredient = this.selectedIngredient();
    const measurementId = this.selectedMeasurementId();
    const hId = this.householdId();

    if (!ingredient || !measurementId || !hId) return;

    this.adding.set(true);

    const today = new Date();
    const dept = categorizeDepartment(ingredient.name);
    const shelfLife = shelfLifeDaysFor(dept);
    const expDate = new Date(today);
    expDate.setDate(expDate.getDate() + shelfLife);

    const request: PantryItemCreateRequest = {
      householdId: hId,
      ingredientId: ingredient.id,
      quantity: this.newQuantity(),
      measurementId,
      acquisitionDate: this.formatDate(today),
      expectedExpirationDate: this.formatDate(expDate),
    };

    this.pantryService
      .addPantryItem(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (item) => {
          this.items.update((items) => [...items, item]);
          this.resetAddForm();
          this.showAddForm.set(false);
          this.adding.set(false);
        },
        error: () => {
          this.adding.set(false);
        },
      });
  }

  removeItem(id: number) {
    this.pantryService
      .removePantryItem(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.items.update((items) => items.filter((i) => i.id !== id));
        },
      });
  }

  refresh() {
    this.loadPantryItems();
  }

  formatQuantity(qty: number): string {
    return formatQuantity(qty);
  }

  private resetAddForm() {
    this.ingredientSearch.set('');
    this.selectedIngredient.set(null);
    this.newQuantity.set(1);
    this.selectedMeasurementId.set(null);
    this.ingredientOptions.set([]);
  }

  private formatDate(d: Date): string {
    return toLocalDateString(d);
  }
}
