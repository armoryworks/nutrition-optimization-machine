import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, of } from 'rxjs';
import { catchError, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { MatAutocomplete, MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { IngredientService } from '../core/services/ingredient.service';
import { HouseholdShoppingService } from '../core/services/household-shopping.service';
import { IngredientSearchResult } from '../core/models/ingredient-search-result.model';
import { HouseholdShoppingItem } from '../core/models/household-shopping.model';
import { makeRoomForAutocomplete } from '../core/utils/autocomplete-room';

export type AddItemOption =
  | { kind: 'catalog'; hit: IngredientSearchResult }
  | { kind: 'new'; name: string };

/**
 * Catalog hits for what was typed, followed by an "add as new" choice unless a
 * hit already carries exactly that name.
 */
export function addItemOptions(query: string, hits: IngredientSearchResult[]): AddItemOption[] {
  const name = query.trim();
  const options: AddItemOption[] = hits.map((hit) => ({ kind: 'catalog', hit }));
  if (name.length >= 2 && !hits.some((h) => h.name.toLowerCase() === name.toLowerCase())) {
    options.push({ kind: 'new', name });
  }
  return options;
}

@Component({
  selector: 'nom-shopping-add-item',
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './shopping-add-item.component.html',
  styleUrl: './shopping-add-item.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShoppingAddItem {
  householdId = input.required<number>();
  existingIngredientIds = input<ReadonlySet<number>>(new Set());
  disabled = input(false);
  added = output<HouseholdShoppingItem>();

  private ingredientService = inject(IngredientService);
  private shoppingService = inject(HouseholdShoppingService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);
  private searchSubject = new Subject<string>();
  private autocomplete = viewChild(MatAutocomplete);
  private inputEl = viewChild<ElementRef<HTMLInputElement>>('itemInput');

  query = signal('');
  hits = signal<IngredientSearchResult[]>([]);
  options = signal<AddItemOption[]>([]);
  saving = signal(false);
  error = signal('');

  constructor() {
    this.searchSubject
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) =>
          q.trim().length < 2
            ? of([] as IngredientSearchResult[])
            : this.ingredientService.searchIngredients(q.trim()).pipe(catchError(() => of([]))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((hits) => {
        this.hits.set(hits);
        this.options.set(addItemOptions(this.query(), hits));
        this.scrollPanelToTop();
      });
  }

  onQueryChange(value: string): void {
    this.query.set(value);
    this.error.set('');
    this.options.set(addItemOptions(value, value.trim().length < 2 ? [] : this.hits()));
    this.searchSubject.next(value);
  }

  onFocus(event: FocusEvent): void {
    makeRoomForAutocomplete(event.target, window);
  }

  onOptionSelected(event: MatAutocompleteSelectedEvent): void {
    this.add(event.option.value as AddItemOption);
  }

  submit(): void {
    const name = this.query().trim();
    if (!name) return;
    const exact = this.hits().find((h) => h.name.toLowerCase() === name.toLowerCase());
    this.add(exact ? { kind: 'catalog', hit: exact } : { kind: 'new', name });
  }

  onEnter(event: Event): void {
    if (this.autocomplete()?.isOpen) return;
    event.preventDefault();
    this.submit();
  }

  displayWith = (): string => '';

  scrollPanelToTop(): void {
    setTimeout(() => {
      const auto = this.autocomplete();
      if (auto?.isOpen && auto.panel) auto.panel.nativeElement.scrollTop = 0;
    }, 80);
  }

  private add(option: AddItemOption): void {
    if (this.saving()) return;
    const name = option.kind === 'catalog' ? option.hit.name : option.name;
    const ingredientId = option.kind === 'catalog' ? option.hit.id : null;

    if (ingredientId !== null && this.existingIngredientIds().has(ingredientId)) {
      this.snackBar.open(`${name} is already on your list`, 'OK', { duration: 3000 });
      this.reset();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.shoppingService
      .addItem({ householdId: this.householdId(), name, ingredientId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (item) => {
          this.saving.set(false);
          this.added.emit(item);
          this.reset();
          this.snackBar.open(`Added ${item.name}`, 'OK', { duration: 2000 });
        },
        error: () => {
          this.saving.set(false);
          this.error.set(`Could not add ${name} — check your connection and try again`);
        },
      });
  }

  private reset(): void {
    this.query.set('');
    this.hits.set([]);
    this.options.set([]);
    const el = this.inputEl()?.nativeElement;
    if (el) {
      el.value = '';
      el.focus();
    }
  }
}
