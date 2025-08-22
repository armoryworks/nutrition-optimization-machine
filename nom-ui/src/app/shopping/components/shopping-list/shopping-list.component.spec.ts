import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { ShoppingListComponent } from './shopping-list.component';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { of } from 'rxjs';

describe('ShoppingListComponent', () => {
    let component: ShoppingListComponent;
    let fixture: ComponentFixture<ShoppingListComponent>;
    let mockShoppingReferenceService: jasmine.SpyObj<ShoppingReferenceService>;

    const mockPriorities = [
        { referenceId: 11000, referenceName: 'Low', referenceDescription: 'Low priority items', groupId: 6000, groupName: 'Shopping Priority', groupDescription: 'Shopping priority levels' },
        { referenceId: 11001, referenceName: 'Medium', referenceDescription: 'Medium priority items', groupId: 6000, groupName: 'Shopping Priority', groupDescription: 'Shopping priority levels' },
        { referenceId: 11002, referenceName: 'High', referenceDescription: 'High priority items', groupId: 6000, groupName: 'Shopping Priority', groupDescription: 'Shopping priority levels' }
    ];

    const mockCategories = [
        { referenceId: 11010, referenceName: 'Produce', referenceDescription: 'Fresh produce items', groupId: 6001, groupName: 'Shopping Category', groupDescription: 'Shopping categories' },
        { referenceId: 11011, referenceName: 'Dairy', referenceDescription: 'Dairy products', groupId: 6001, groupName: 'Shopping Category', groupDescription: 'Shopping categories' },
        { referenceId: 11012, referenceName: 'Meat', referenceDescription: 'Meat and poultry', groupId: 6001, groupName: 'Shopping Category', groupDescription: 'Shopping categories' }
    ];

    const mockShoppingItems = [
        { id: 1, name: 'Apples', categoryId: 11010, priorityId: 11000, quantity: 6 },
        { id: 2, name: 'Milk', categoryId: 11011, priorityId: 11001, quantity: 1 },
        { id: 3, name: 'Chicken Breast', categoryId: 11012, priorityId: 11002, quantity: 2 }
    ];

    beforeEach(async () => {
        mockShoppingReferenceService = jasmine.createSpyObj('ShoppingReferenceService', [
            'getShoppingReferencesBulk'
        ]);

        await TestBed.configureTestingModule({
            declarations: [ShoppingListComponent],
            imports: [
                ReactiveFormsModule,
                MatFormFieldModule,
                MatSelectModule,
                MatInputModule,
                MatButtonModule,
                MatIconModule,
                BrowserAnimationsModule
            ],
            providers: [
                FormBuilder,
                { provide: ShoppingReferenceService, useValue: mockShoppingReferenceService }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(ShoppingListComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should initialize with empty arrays', () => {
        expect(component.priorities).toEqual([]);
        expect(component.categories).toEqual([]);
        expect(component.filteredItems).toEqual([]);
        expect(component.allItems).toEqual([]);
    });

    it('should load reference data on init', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        expect(mockShoppingReferenceService.getShoppingReferencesBulk).toHaveBeenCalled();
        expect(component.priorities).toEqual(mockPriorities);
        expect(component.categories).toEqual(mockCategories);
    });

    it('should load shopping items on init', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        expect(component.allItems.length).toBeGreaterThan(0);
        expect(component.filteredItems.length).toBeGreaterThan(0);
    });

    it('should setup filter form correctly', () => {
        expect(component.filtersForm).toBeDefined();
        expect(component.filtersForm.get('priorityFilter')).toBeDefined();
        expect(component.filtersForm.get('categoryFilter')).toBeDefined();
    });

    it('should filter items by priority', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Set priority filter
        component.filtersForm.get('priorityFilter')?.setValue(11002); // High priority
        fixture.detectChanges();

        // Should only show high priority items
        const highPriorityItems = component.filteredItems.filter(item => item.priorityId === 11002);
        expect(component.filteredItems.length).toBe(highPriorityItems.length);
    });

    it('should filter items by category', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Set category filter
        component.filtersForm.get('categoryFilter')?.setValue(11010); // Produce
        fixture.detectChanges();

        // Should only show produce items
        const produceItems = component.filteredItems.filter(item => item.categoryId === 11010);
        expect(component.filteredItems.length).toBe(produceItems.length);
    });

    it('should filter items by both priority and category', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Set both filters
        component.filtersForm.get('priorityFilter')?.setValue(11001); // Medium priority
        component.filtersForm.get('categoryFilter')?.setValue(11011); // Dairy
        fixture.detectChanges();

        // Should only show medium priority dairy items
        const filteredItems = component.filteredItems.filter(item =>
            item.priorityId === 11001 && item.categoryId === 11011
        );
        expect(component.filteredItems.length).toBe(filteredItems.length);
    });

    it('should clear filters correctly', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Set filters
        component.filtersForm.get('priorityFilter')?.setValue(11002);
        component.filtersForm.get('categoryFilter')?.setValue(11010);

        // Clear filters
        component.clearFilters();

        expect(component.filtersForm.get('priorityFilter')?.value).toBe('');
        expect(component.filtersForm.get('categoryFilter')?.value).toBe('');
    });

    it('should detect active filters correctly', () => {
        expect(component.hasActiveFilters).toBe(false);

        component.filtersForm.get('priorityFilter')?.setValue(11002);
        expect(component.hasActiveFilters).toBe(true);

        component.filtersForm.get('priorityFilter')?.setValue('');
        component.filtersForm.get('categoryFilter')?.setValue(11010);
        expect(component.hasActiveFilters).toBe(true);
    });

    it('should get priority name correctly', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        expect(component.getPriorityName(11002)).toBe('High');
        expect(component.getPriorityName(999)).toBe('Unknown'); // Non-existent ID
    });

    it('should get category name correctly', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        expect(component.getCategoryName(11010)).toBe('Produce');
        expect(component.getCategoryName(999)).toBe('Unknown'); // Non-existent ID
    });

    it('should get priority class correctly', () => {
        expect(component.getPriorityClass(11002)).toBe('high');
        expect(component.getPriorityClass(11001)).toBe('medium');
        expect(component.getPriorityClass(11000)).toBe('low');
    });

    it('should get priority color correctly', () => {
        expect(component.getPriorityColor(11002)).toBe('#f44336'); // Red for high
        expect(component.getPriorityColor(11001)).toBe('#ff9800'); // Orange for medium
        expect(component.getPriorityColor(11000)).toBe('#4caf50'); // Green for low
    });

    it('should generate consistent category colors', () => {
        const color1 = component.getCategoryColor(11010);
        const color2 = component.getCategoryColor(11010);
        expect(color1).toBe(color2); // Same ID should get same color
    });

    it('should count high priority items correctly', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        const highPriorityCount = component.getHighPriorityCount();
        const expectedCount = component.allItems.filter(item => item.priorityId === 11002).length;
        expect(highPriorityCount).toBe(expectedCount);
    });

    it('should count unique categories correctly', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        const uniqueCategoriesCount = component.getUniqueCategoriesCount();
        const expectedCount = new Set(component.allItems.map(item => item.categoryId)).size;
        expect(uniqueCategoriesCount).toBe(expectedCount);
    });

    it('should handle form value changes', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Simulate form value change
        component.filtersForm.get('priorityFilter')?.setValue(11002);

        // Should trigger filtering
        expect(component.filteredItems.length).toBeLessThanOrEqual(component.allItems.length);
    });

    it('should handle priority filter change', () => {
        expect(() => component.onPriorityFilterChange({})).not.toThrow();
    });

    it('should handle category filter change', () => {
        expect(() => component.onCategoryFilterChange({})).not.toThrow();
    });

    it('should handle add new item', () => {
        spyOn(console, 'log');
        component.addNewItem();
        expect(console.log).toHaveBeenCalledWith('Add new item clicked');
    });

    it('should handle edit item', () => {
        spyOn(console, 'log');
        const testItem = { id: 1, name: 'Test Item' };
        component.editItem(testItem);
        expect(console.log).toHaveBeenCalledWith('Edit item:', testItem);
    });

    it('should handle delete item', () => {
        spyOn(console, 'log');
        const testItem = { id: 1, name: 'Test Item' };
        component.deleteItem(testItem);
        expect(console.log).toHaveBeenCalledWith('Delete item:', testItem);
    });

    it('should clean up subscriptions on destroy', () => {
        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(
            of({ priorities: mockPriorities, categories: mockCategories })
        );

        fixture.detectChanges();

        // Should not throw on destroy
        expect(() => component.ngOnDestroy()).not.toThrow();
    });
});
