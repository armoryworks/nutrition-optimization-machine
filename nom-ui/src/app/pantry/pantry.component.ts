import { Component, computed, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
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

/** Department-based default shelf life in days */
const SHELF_LIFE_DEFAULTS: Record<string, number> = {
  'produce': 5,
  'meat & seafood': 3,
  'dairy & eggs': 10,
  'bakery': 5,
  'grains & pasta': 180,
  'canned & jarred': 365,
  'condiments & sauces': 90,
  'spices & seasonings': 365,
  'oils & vinegars': 180,
  'baking': 180,
  'frozen': 90,
  'beverages': 30,
  'other': 90,
};

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
    this.items().filter(i => !i.isExpired && i.statusName === 'In Pantry')
  );

  expiredItems = computed(() =>
    this.items().filter(i => i.isExpired)
  );

  expiringSoonItems = computed(() =>
    this.activeItems().filter(i => i.isExpiringSoon)
  );

  ngOnInit() {
    this.loadHouseholdThenItems();
    this.loadMeasurements();
    this.setupIngredientSearch();
  }

  private loadHouseholdThenItems() {
    this.loading.set(true);
    this.error.set(null);

    this.householdService.getHouseholds().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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

    this.pantryService.getPantryItems(this.householdId()).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
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
    this.measurementService.loadMeasurements().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (m) => this.measurements.set(m),
    });
  }

  private setupIngredientSearch() {
    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(query => {
        if (query.length < 2) return of([]);
        return this.ingredientService.searchIngredients(query);
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(options => this.ingredientOptions.set(options));
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
    this.showAddForm.update(v => !v);
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
    const dept = this.categorizeDepartment(ingredient.name);
    const shelfLife = SHELF_LIFE_DEFAULTS[dept.toLowerCase()] ?? 90;
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

    this.pantryService.addPantryItem(request).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (item) => {
        this.items.update(items => [...items, item]);
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
    this.pantryService.removePantryItem(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.items.update(items => items.filter(i => i.id !== id));
      },
    });
  }

  refresh() {
    this.loadPantryItems();
  }

  formatQuantity(qty: number): string {
    return qty % 1 === 0 ? qty.toString() : qty.toFixed(2).replace(/0+$/, '');
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

  private categorizeDepartment(name: string): string {
    const n = name.toLowerCase();
    if (/chicken|beef|pork|salmon|shrimp|turkey|fish|steak|bacon|sausage|lamb|tuna|crab|lobster|ham|prosciutto|pancetta|anchov/i.test(n)) return 'Meat & Seafood';
    if (/milk|cream|cheese|yogurt|butter|egg|sour cream|whipping|half.and.half|ricotta|mozzarella|parmesan|cheddar|feta|cream cheese/i.test(n)) return 'Dairy & Eggs';
    if (/lettuce|tomato|onion|garlic|pepper|carrot|celery|potato|spinach|broccoli|cucumber|avocado|lemon|lime|orange|apple|banana|berry|berries|mushroom|zucchini|squash|corn|pea|bean.*fresh|herb|cilantro|parsley|basil|mint|thyme|rosemary|ginger|jalap|scallion|shallot|cabbage|kale|arugula/i.test(n)) return 'Produce';
    if (/rice|pasta|noodle|spaghetti|penne|macaroni|fettuccine|linguine|bread|tortilla|bun|roll|pita|couscous|quinoa|barley|oat|cereal|granola/i.test(n)) return 'Grains & Pasta';
    if (/flour|sugar|baking soda|baking powder|yeast|cocoa|chocolate chip|vanilla extract|cornstarch|powdered sugar|brown sugar|molasses/i.test(n)) return 'Baking';
    if (/salt|pepper|cumin|paprika|oregano|cinnamon|nutmeg|chili powder|curry|turmeric|cayenne|thyme.*dried|basil.*dried|garlic powder|onion powder|bay lea|clove|allspice|cardamom|coriander|dill.*dried|fennel.*seed|mustard.*seed|red pepper flake|saffron|seasoning/i.test(n)) return 'Spices & Seasonings';
    if (/olive oil|vegetable oil|canola oil|coconut oil|sesame oil|vinegar|cooking spray/i.test(n)) return 'Oils & Vinegars';
    if (/ketchup|mustard|mayo|soy sauce|hot sauce|worcestershire|bbq sauce|salsa|dressing|relish|horseradish|sriracha|tahini|pesto|teriyaki|hoisin|fish sauce|oyster sauce/i.test(n)) return 'Condiments & Sauces';
    if (/canned|tomato paste|tomato sauce|diced tomato|crushed tomato|broth|stock|bean.*canned|chickpea|lentil|coconut milk|condensed|jar|preserve|jam|jelly|pickle|olive.*jar|artichoke|sun.dried/i.test(n)) return 'Canned & Jarred';
    if (/frozen|ice cream/i.test(n)) return 'Frozen';
    if (/juice|water|soda|coffee|tea|wine|beer|kombucha/i.test(n)) return 'Beverages';
    return 'Other';
  }
}
