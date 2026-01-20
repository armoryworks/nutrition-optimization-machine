import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimations } from '@angular/platform-browser/animations';
import { ReactiveFormsModule } from '@angular/forms';
import { ShoppingListComponent } from './shopping-list.component';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { of } from 'rxjs';

describe('ShoppingListComponent', () => {
    let component: ShoppingListComponent;
    let fixture: ComponentFixture<ShoppingListComponent>;
    let mockShoppingReferenceService: jasmine.SpyObj<ShoppingReferenceService>;
    let mockConfigurationService: jasmine.SpyObj<ConfigurationService>;

    const mockPriorities = [
        { referenceId: 11000, referenceName: 'Low', referenceDescription: 'Low priority items' },
        { referenceId: 11001, referenceName: 'Medium', referenceDescription: 'Medium priority items' },
        { referenceId: 11002, referenceName: 'High', referenceDescription: 'High priority items' }
    ];

    const mockCategories = [
        { referenceId: 1, referenceName: 'Produce', referenceDescription: 'Fresh fruits and vegetables' },
        { referenceId: 2, referenceName: 'Dairy', referenceDescription: 'Milk, cheese, and dairy products' }
    ];

    beforeEach(async () => {
        mockShoppingReferenceService = jasmine.createSpyObj('ShoppingReferenceService', [
            'getShoppingReferencesBulk'
        ]);
        mockConfigurationService = jasmine.createSpyObj('ConfigurationService', ['getCategoryColor']);

        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(of({
            priorities: mockPriorities,
            categories: mockCategories
        }));
        mockConfigurationService.getCategoryColor.and.returnValue('#1976d2');

        await TestBed.configureTestingModule({
            imports: [
                ShoppingListComponent,
                ReactiveFormsModule
            ],
            providers: [
                provideAnimations(),
                { provide: ShoppingReferenceService, useValue: mockShoppingReferenceService },
                { provide: ConfigurationService, useValue: mockConfigurationService }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(ShoppingListComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should initialize with default values', () => {
        expect(component.filteredItems()).toEqual([]);
        expect(component.categories()).toEqual([]);
        expect(component.priorities()).toEqual([]);
    });

    it('should load reference data on init', () => {
        fixture.detectChanges();

        expect(mockShoppingReferenceService.getShoppingReferencesBulk).toHaveBeenCalled();
    });

    it('should populate priorities and categories signals', () => {
        fixture.detectChanges();

        expect(component.priorities()).toEqual(mockPriorities);
        expect(component.categories()).toEqual(mockCategories);
    });

    it('should filter items by priority', () => {
        fixture.detectChanges();

        const mockItems = [
            { id: 1, name: 'Apples', priorityId: 11000, categoryId: 1 },
            { id: 2, name: 'Milk', priorityId: 11002, categoryId: 2 }
        ];
        component.allItems.set(mockItems);
        component.filtersForm.patchValue({ priorityFilter: 11002 });

        expect(component.filteredItems().length).toBe(1);
        expect(component.filteredItems()[0].priorityId).toBe(11002);
    });

    it('should filter items by category', () => {
        fixture.detectChanges();

        const mockItems = [
            { id: 1, name: 'Apples', priorityId: 11000, categoryId: 1 },
            { id: 2, name: 'Milk', priorityId: 11002, categoryId: 2 }
        ];
        component.allItems.set(mockItems);
        component.filtersForm.patchValue({ categoryFilter: 1 });

        expect(component.filteredItems().length).toBe(1);
        expect(component.filteredItems()[0].categoryId).toBe(1);
    });

    it('should combine multiple filters', () => {
        fixture.detectChanges();

        const mockItems = [
            { id: 1, name: 'Apples', priorityId: 11000, categoryId: 1 },
            { id: 2, name: 'Milk', priorityId: 11002, categoryId: 2 },
            { id: 3, name: 'Oranges', priorityId: 11002, categoryId: 1 }
        ];
        component.allItems.set(mockItems);
        component.filtersForm.patchValue({ priorityFilter: 11002, categoryFilter: 1 });

        expect(component.filteredItems().length).toBe(1);
        expect(component.filteredItems()[0].name).toBe('Oranges');
    });

    it('should clear all filters', () => {
        fixture.detectChanges();

        component.filtersForm.patchValue({ priorityFilter: 11002, categoryFilter: 1 });
        component.clearFilters();

        expect(component.filtersForm.get('priorityFilter')?.value).toBe('');
        expect(component.filtersForm.get('categoryFilter')?.value).toBe('');
    });

    it('should get priority name by id', () => {
        fixture.detectChanges();

        const priorityName = component.getPriorityName(11002);
        expect(priorityName).toBe('High');
    });

    it('should return unknown for invalid priority id', () => {
        fixture.detectChanges();

        const priorityName = component.getPriorityName(999);
        expect(priorityName).toBe('Unknown');
    });

    it('should get category name by id', () => {
        fixture.detectChanges();

        const categoryName = component.getCategoryName(1);
        expect(categoryName).toBe('Produce');
    });

    it('should return unknown for invalid category id', () => {
        fixture.detectChanges();

        const categoryName = component.getCategoryName(999);
        expect(categoryName).toBe('Unknown');
    });

    it('should get priority color correctly', () => {
        fixture.detectChanges();

        const highColor = component.getPriorityColor(11002);
        expect(highColor).toBe('#f44336'); // Red for high

        const mediumColor = component.getPriorityColor(11001);
        expect(mediumColor).toBe('#ff9800'); // Orange for medium

        const lowColor = component.getPriorityColor(11000);
        expect(lowColor).toBe('#4caf50'); // Green for low
    });

    it('should get category color from configuration service', () => {
        fixture.detectChanges();

        const color = component.getCategoryColor(1);
        expect(mockConfigurationService.getCategoryColor).toHaveBeenCalledWith(1);
        expect(color).toBe('#1976d2');
    });

    it('should detect active filters', () => {
        fixture.detectChanges();

        expect(component.hasActiveFilters).toBe(false);

        component.filtersForm.patchValue({ priorityFilter: 11002 });
        expect(component.hasActiveFilters).toBe(true);
    });

    it('should get high priority count', () => {
        fixture.detectChanges();

        const mockItems = [
            { id: 1, name: 'Apples', priorityId: 11000, categoryId: 1 },
            { id: 2, name: 'Milk', priorityId: 11002, categoryId: 2 },
            { id: 3, name: 'Bread', priorityId: 11002, categoryId: 1 }
        ];
        component.allItems.set(mockItems);

        expect(component.getHighPriorityCount()).toBe(2);
    });

    it('should get unique categories count', () => {
        fixture.detectChanges();

        const mockItems = [
            { id: 1, name: 'Apples', priorityId: 11000, categoryId: 1 },
            { id: 2, name: 'Milk', priorityId: 11002, categoryId: 2 },
            { id: 3, name: 'Bread', priorityId: 11002, categoryId: 1 }
        ];
        component.allItems.set(mockItems);

        expect(component.getUniqueCategoriesCount()).toBe(2);
    });

    it('should clean up subscriptions on destroy', () => {
        fixture.detectChanges();

        // Should not throw on destroy
        expect(() => component.ngOnDestroy()).not.toThrow();
    });
});
