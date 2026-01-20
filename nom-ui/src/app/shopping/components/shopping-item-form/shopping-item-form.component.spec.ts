import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideAnimations } from '@angular/platform-browser/animations';
import { ShoppingItemFormComponent } from './shopping-item-form.component';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { of } from 'rxjs';

describe('ShoppingItemFormComponent', () => {
    let component: ShoppingItemFormComponent;
    let fixture: ComponentFixture<ShoppingItemFormComponent>;
    let mockShoppingReferenceService: jasmine.SpyObj<ShoppingReferenceService>;
    let mockReferenceDataService: jasmine.SpyObj<ReferenceDataService>;

    const mockCategories = [
        { referenceId: 1, referenceName: 'Produce', referenceDescription: 'Fresh fruits and vegetables' },
        { referenceId: 2, referenceName: 'Dairy', referenceDescription: 'Milk, cheese, and dairy products' }
    ];

    const mockPriorities = [
        { referenceId: 1, referenceName: 'Low', referenceDescription: 'Low priority items' },
        { referenceId: 2, referenceName: 'High', referenceDescription: 'High priority items' }
    ];

    beforeEach(async () => {
        mockShoppingReferenceService = jasmine.createSpyObj('ShoppingReferenceService', [
            'getShoppingReferencesBulk'
        ]);
        mockReferenceDataService = jasmine.createSpyObj('ReferenceDataService', [
            'getReferencesByGroup'
        ]);

        mockShoppingReferenceService.getShoppingReferencesBulk.and.returnValue(of({
            priorities: mockPriorities,
            categories: mockCategories
        }));
        mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockCategories));

        await TestBed.configureTestingModule({
            imports: [
                ShoppingItemFormComponent,
                ReactiveFormsModule
            ],
            providers: [
                provideAnimations(),
                { provide: ShoppingReferenceService, useValue: mockShoppingReferenceService },
                { provide: ReferenceDataService, useValue: mockReferenceDataService }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(ShoppingItemFormComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should initialize form with default values', () => {
        expect(component.shoppingItemForm).toBeDefined();
        expect(component.shoppingItemForm.get('name')?.value).toBe('');
        expect(component.shoppingItemForm.get('quantity')?.value).toBe(1);
        expect(component.shoppingItemForm.get('categoryId')?.value).toBeNull();
        expect(component.shoppingItemForm.get('priorityId')?.value).toBeNull();
    });

    it('should load shopping references on init', () => {
        fixture.detectChanges();

        expect(mockShoppingReferenceService.getShoppingReferencesBulk).toHaveBeenCalled();
    });

    it('should validate required fields', () => {
        const nameControl = component.shoppingItemForm.get('name');
        const quantityControl = component.shoppingItemForm.get('quantity');

        expect(nameControl?.hasError('required')).toBeTruthy();
        expect(quantityControl?.hasError('required')).toBeFalsy(); // Has default value
    });

    it('should validate quantity constraints', () => {
        const quantityControl = component.shoppingItemForm.get('quantity');

        quantityControl?.setValue(0); // Too low
        expect(quantityControl?.hasError('min')).toBeTruthy();

        quantityControl?.setValue(50); // Valid
        expect(quantityControl?.errors).toBeNull();
    });

    it('should handle form submission when valid', () => {
        fixture.detectChanges();

        spyOn(console, 'log');
        spyOn(window, 'alert');

        component.shoppingItemForm.patchValue({
            name: 'Test Item',
            quantity: 5,
            categoryId: 1,
            priorityId: 2
        });

        component.onSubmit();

        expect(console.log).toHaveBeenCalledWith('Form submitted:', jasmine.any(Object));
        expect(window.alert).toHaveBeenCalled();
    });

    it('should not emit form submission when invalid', () => {
        fixture.detectChanges();

        spyOn(console, 'log');
        spyOn(window, 'alert');

        component.shoppingItemForm.patchValue({
            name: '', // Invalid - required field empty
            quantity: 5
        });

        component.onSubmit();

        expect(console.log).not.toHaveBeenCalled();
        expect(window.alert).not.toHaveBeenCalled();
    });

    it('should reset form after successful submission', () => {
        fixture.detectChanges();

        spyOn(window, 'alert');

        component.shoppingItemForm.patchValue({
            name: 'Test Item',
            quantity: 5,
            categoryId: 1,
            priorityId: 2
        });

        component.onSubmit();

        expect(component.shoppingItemForm.get('name')?.value).toBe('');
        expect(component.shoppingItemForm.get('quantity')?.value).toBe(1);
        expect(component.shoppingItemForm.get('categoryId')?.value).toBeNull();
        expect(component.shoppingItemForm.get('priorityId')?.value).toBeNull();
    });

    it('should handle form cancel', () => {
        fixture.detectChanges();

        component.shoppingItemForm.patchValue({
            name: 'Test Item',
            quantity: 5
        });

        component.onCancel();

        expect(component.shoppingItemForm.get('name')?.value).toBe('');
        expect(component.shoppingItemForm.get('quantity')?.value).toBe(1);
        expect(component.selectedCategory()).toBeUndefined();
        expect(component.selectedPriority()).toBeUndefined();
    });

    it('should update selectedCategory signal on category change', () => {
        fixture.detectChanges();

        const mockCategory = { referenceId: 1, referenceName: 'Produce', referenceDescription: 'Fresh fruits and vegetables' };
        component.onCategoryChange(mockCategory as any);

        expect(component.selectedCategory()).toEqual(mockCategory as any);
    });

    it('should update selectedPriority signal on priority change', () => {
        fixture.detectChanges();

        const mockPriority = { referenceId: 2, referenceName: 'High', referenceDescription: 'High priority items' };
        component.onPriorityChange(mockPriority as any);

        expect(component.selectedPriority()).toEqual(mockPriority as any);
    });

    it('should clean up subscriptions on destroy', () => {
        fixture.detectChanges();

        // Should not throw on destroy
        expect(() => component.ngOnDestroy()).not.toThrow();
    });
});
