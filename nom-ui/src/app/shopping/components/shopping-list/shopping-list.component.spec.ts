import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatMenuModule } from '@angular/material/menu';
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

    const mockCategories = [
        { referenceId: 1, referenceName: 'Produce', referenceDescription: 'Fresh fruits and vegetables' },
        { referenceId: 2, referenceName: 'Dairy', referenceDescription: 'Milk, cheese, and dairy products' }
    ];

    const mockPriorities = [
        { referenceId: 1, referenceName: 'Low', referenceDescription: 'Low priority items' },
        { referenceId: 2, referenceName: 'High', referenceDescription: 'High priority items' }
    ];

    const mockItems = [
        {
            id: 1,
            name: 'Apples',
            quantity: 5,
            unit: 'kg',
            categoryId: 1,
            priorityId: 1,
            notes: 'Fresh red apples',
            isCompleted: false
        },
        {
            id: 2,
            name: 'Milk',
            quantity: 2,
            unit: 'L',
            categoryId: 2,
            priorityId: 2,
            notes: 'Whole milk',
            isCompleted: true
        }
    ];

    beforeEach(async () => {
        mockShoppingReferenceService = jasmine.createSpyObj('ShoppingReferenceService', [
            'getShoppingCategories',
            'getShoppingPriorities'
        ]);
        mockConfigurationService = jasmine.createSpyObj('ConfigurationService', ['getColorPalette']);

        mockShoppingReferenceService.getShoppingCategories.and.returnValue(of(mockCategories));
        mockShoppingReferenceService.getShoppingPriorities.and.returnValue(of(mockPriorities));
        mockConfigurationService.getColorPalette.and.returnValue(['#1976d2', '#388e3c', '#f57c00', '#d32f2f']);

        await TestBed.configureTestingModule({
            declarations: [ShoppingListComponent],
            imports: [
                BrowserAnimationsModule,
                MatCardModule,
                MatButtonModule,
                MatIconModule,
                MatChipsModule,
                MatFormFieldModule,
                MatInputModule,
                MatSelectModule,
                MatMenuModule,
                ReactiveFormsModule
            ],
            providers: [
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
        expect(component.items).toEqual([]);
        expect(component.filteredItems).toEqual([]);
        expect(component.categories).toEqual([]);
        expect(component.priorities).toEqual([]);
        expect(component.selectedCategory).toBeNull();
        expect(component.selectedPriority).toBeNull();
        expect(component.searchTerm).toBe('');
        expect(component.showCompleted).toBe(true);
    });

    it('should load reference data on init', () => {
        component.ngOnInit();

        expect(mockShoppingReferenceService.getShoppingCategories).toHaveBeenCalled();
        expect(mockShoppingReferenceService.getShoppingPriorities).toHaveBeenCalled();
        expect(mockConfigurationService.getColorPalette).toHaveBeenCalled();
    });

    it('should populate categories and priorities arrays', () => {
        component.ngOnInit();

        expect(component.categories).toEqual(mockCategories);
        expect(component.priorities).toEqual(mockPriorities);
    });

    it('should set items and filtered items', () => {
        component.items = mockItems;
        component.ngOnInit();

        expect(component.filteredItems).toEqual(mockItems);
    });

    it('should filter items by category', () => {
        component.items = mockItems;
        component.categories = mockCategories;
        component.selectedCategory = 1;

        component.filterItems();

        expect(component.filteredItems.length).toBe(1);
        expect(component.filteredItems[0].categoryId).toBe(1);
    });

    it('should filter items by priority', () => {
        component.items = mockItems;
        component.priorities = mockPriorities;
        component.selectedPriority = 2;

        component.filterItems();

        expect(component.filteredItems.length).toBe(1);
        expect(component.filteredItems[0].priorityId).toBe(2);
    });

    it('should filter items by search term', () => {
        component.items = mockItems;
        component.searchTerm = 'apples';

        component.filterItems();

        expect(component.filteredItems.length).toBe(1);
        expect(component.filteredItems[0].name.toLowerCase()).toContain('apples');
    });

    it('should filter completed items', () => {
        component.items = mockItems;
        component.showCompleted = false;

        component.filterItems();

        expect(component.filteredItems.length).toBe(1);
        expect(component.filteredItems[0].isCompleted).toBe(false);
    });

    it('should combine multiple filters', () => {
        component.items = mockItems;
        component.categories = mockCategories;
        component.priorities = mockPriorities;
        component.selectedCategory = 1;
        component.selectedPriority = 1;
        component.searchTerm = 'apples';
        component.showCompleted = false;

        component.filterItems();

        expect(component.filteredItems.length).toBe(1);
        expect(component.filteredItems[0].name).toBe('Apples');
        expect(component.filteredItems[0].categoryId).toBe(1);
        expect(component.filteredItems[0].priorityId).toBe(1);
        expect(component.filteredItems[0].isCompleted).toBe(false);
    });

    it('should clear all filters', () => {
        component.selectedCategory = 1;
        component.selectedPriority = 2;
        component.searchTerm = 'test';
        component.showCompleted = false;

        component.clearFilters();

        expect(component.selectedCategory).toBeNull();
        expect(component.selectedPriority).toBeNull();
        expect(component.searchTerm).toBe('');
        expect(component.showCompleted).toBe(true);
    });

    it('should get category name by id', () => {
        component.categories = mockCategories;

        const categoryName = component.getCategoryName(1);
        expect(categoryName).toBe('Produce');
    });

    it('should return unknown for invalid category id', () => {
        component.categories = mockCategories;

        const categoryName = component.getCategoryName(999);
        expect(categoryName).toBe('Unknown');
    });

    it('should get priority name by id', () => {
        component.priorities = mockPriorities;

        const priorityName = component.getPriorityName(2);
        expect(priorityName).toBe('High');
    });

    it('should return unknown for invalid priority id', () => {
        component.priorities = mockPriorities;

        const priorityName = component.getPriorityName(999);
        expect(priorityName).toBe('Unknown');
    });

    it('should generate color for item', () => {
        component.items = mockItems;
        component.colorPalette = ['#1976d2', '#388e3c', '#f57c00', '#d32f2f'];

        const color = component.getItemColor(mockItems[0]);
        expect(color).toBeDefined();
        expect(component.colorPalette).toContain(color);
    });

    it('should toggle item completion status', () => {
        const item = { ...mockItems[0] };
        spyOn(component.itemStatusChange, 'emit');

        component.toggleItemStatus(item);

        expect(item.isCompleted).toBe(true);
        expect(component.itemStatusChange.emit).toHaveBeenCalledWith(item);
    });

    it('should emit edit item event', () => {
        const item = mockItems[0];
        spyOn(component.editItem, 'emit');

        component.onEditItem(item);

        expect(component.editItem.emit).toHaveBeenCalledWith(item);
    });

    it('should emit delete item event', () => {
        const item = mockItems[0];
        spyOn(component.deleteItem, 'emit');

        component.onDeleteItem(item);

        expect(component.deleteItem.emit).toHaveBeenCalledWith(item);
    });

    it('should emit add item event', () => {
        spyOn(component.addItem, 'emit');

        component.onAddItem();

        expect(component.addItem.emit).toHaveBeenCalled();
    });

    it('should get filtered items count', () => {
        component.filteredItems = mockItems;

        const count = component.getFilteredItemsCount();
        expect(count).toBe(2);
    });

    it('should get completed items count', () => {
        component.items = mockItems;

        const count = component.getCompletedItemsCount();
        expect(count).toBe(1);
    });

    it('should get total items count', () => {
        component.items = mockItems;

        const count = component.getTotalItemsCount();
        expect(count).toBe(2);
    });
});
