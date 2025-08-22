import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { Subject, takeUntil } from 'rxjs';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
  selector: 'app-shopping-list',
  templateUrl: './shopping-list.component.html',
  styleUrls: ['./shopping-list.component.scss']
})
export class ShoppingListComponent implements OnInit, OnDestroy {
  filtersForm: FormGroup;
  priorities: any[] = [];
  categories: any[] = [];
  filteredItems: any[] = [];
  allItems: any[] = []; // This would come from your actual shopping service

  // Make constants available in template
  readonly REFERENCE_IDS = REFERENCE_IDS;

  private destroy$ = new Subject<void>();

  constructor(
    private fb: FormBuilder,
    private shoppingReferenceService: ShoppingReferenceService,
    private configurationService: ConfigurationService
  ) {
    this.filtersForm = this.fb.group({
      priorityFilter: [''],
      categoryFilter: ['']
    });
  }

  ngOnInit(): void {
    this.loadReferenceData();
    this.loadShoppingItems();
    this.setupFilterListeners();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadReferenceData(): void {
    // Load shopping priorities and categories in bulk for performance
    this.shoppingReferenceService.getShoppingReferencesBulk()
      .pipe(takeUntil(this.destroy$))
      .subscribe(({ priorities, categories }) => {
        this.priorities = priorities;
        this.categories = categories;
      });
  }

  private loadShoppingItems(): void {
    // TODO: Replace with actual shopping service call
    // For now, using empty array until shopping service is implemented
    this.allItems = [];
    this.applyFilters();
  }

  private setupFilterListeners(): void {
    this.filtersForm.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.applyFilters();
      });
  }

  private applyFilters(): void {
    let filtered = [...this.allItems];

    const priorityFilter = this.filtersForm.get('priorityFilter')?.value;
    const categoryFilter = this.filtersForm.get('categoryFilter')?.value;

    if (priorityFilter) {
      filtered = filtered.filter(item => item.priorityId === priorityFilter);
    }

    if (categoryFilter) {
      filtered = filtered.filter(item => item.categoryId === categoryFilter);
    }

    this.filteredItems = filtered;
  }

  get hasActiveFilters(): boolean {
    return this.filtersForm.get('priorityFilter')?.value ||
      this.filtersForm.get('categoryFilter')?.value;
  }

  getPriorityName(priorityId: number): string {
    const priority = this.priorities.find(p => p.referenceId === priorityId);
    return priority?.referenceName || 'Unknown';
  }

  getCategoryName(categoryId: number): string {
    const category = this.categories.find(c => c.referenceId === categoryId);
    return category?.referenceName || 'Unknown';
  }

  getPriorityClass(priorityId: number): string {
    if (priorityId === 11002) return 'high';
    if (priorityId === 11001) return 'medium';
    return 'low';
  }

  getPriorityColor(priorityId: number): string {
    // Use dynamic priority data to determine colors
    const priority = this.priorities.find(p => p.referenceId === priorityId);
    if (!priority) return '#9e9e9e'; // Default gray

    // Map priority names to colors dynamically
    const priorityName = priority.referenceName.toLowerCase();
    if (priorityName.includes('high')) return '#f44336'; // Red for high
    if (priorityName.includes('medium')) return '#ff9800'; // Orange for medium
    if (priorityName.includes('low')) return '#4caf50'; // Green for low

    return '#9e9e9e'; // Default gray
  }

  getCategoryColor(categoryId: number): string {
    // Use configuration service for consistent colors
    return this.configurationService.getCategoryColor(categoryId);
  }

  getHighPriorityCount(): number {
    return this.filteredItems.filter(item => item.priorityId === 11002).length;
  }

  getUniqueCategoriesCount(): number {
    const uniqueCategories = new Set(this.filteredItems.map(item => item.categoryId));
    return uniqueCategories.size;
  }

  onPriorityFilterChange(event: any): void {
    // Filter change is handled by the reactive form listener
  }

  onCategoryFilterChange(event: any): void {
    // Filter change is handled by the reactive form listener
  }

  clearFilters(): void {
    this.filtersForm.patchValue({
      priorityFilter: '',
      categoryFilter: ''
    });
  }

  addNewItem(): void {
    // This would open a dialog or navigate to add item form
    console.log('Add new item clicked');
  }

  editItem(item: any): void {
    // This would open edit dialog or navigate to edit form
    console.log('Edit item:', item);
  }

  deleteItem(item: any): void {
    // This would show confirmation dialog and delete item
    console.log('Delete item:', item);
  }
}
