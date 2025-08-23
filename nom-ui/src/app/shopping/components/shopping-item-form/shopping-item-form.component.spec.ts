import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { ShoppingItemFormComponent } from './shopping-item-form.component';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { of } from 'rxjs';

describe('ShoppingItemFormComponent', () => {
    let component: ShoppingItemFormComponent;
    let fixture: ComponentFixture<ShoppingItemFormComponent>;
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

    const mockUnits = ['g', 'kg', 'oz', 'lb'];

    beforeEach(async () => {
        mockShoppingReferenceService = jasmine.createSpyObj('ShoppingReferenceService', [
            'getShoppingCategories',
            'getShoppingPriorities'
        ]);
        mockConfigurationService = jasmine.createSpyObj('ConfigurationService', ['getMassUnits']);

        mockShoppingReferenceService.getShoppingCategories.and.returnValue(of(mockCategories));
        mockShoppingReferenceService.getShoppingPriorities.and.returnValue(of(mockPriorities));
        mockConfigurationService.getMassUnits.and.returnValue(mockUnits);

        await TestBed.configureTestingModule({
            declarations: [ShoppingItemFormComponent],
            imports: [
                ReactiveFormsModule,
                BrowserAnimationsModule,
                MatFormFieldModule,
                MatInputModule,
                MatButtonModule,
                MatSelectModule,
                MatIconModule,
                MatCardModule
            ],
            providers: [
                { provide: ShoppingReferenceService, useValue: mockShoppingReferenceService },
                { provide: ConfigurationService, useValue: mockConfigurationService }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(ShoppingItemFormComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should initialize form with default values', () => {
        expect(component.itemForm).toBeDefined();
        expect(component.itemForm.get('name')?.value).toBe('');
        expect(component.itemForm.get('quantity')?.value).toBe(1);
        expect(component.itemForm.get('unit')?.value).toBe('g');
        expect(component.itemForm.get('categoryId')?.value).toBeNull();
        expect(component.itemForm.get('priorityId')?.value).toBeNull();
        expect(component.itemForm.get('notes')?.value).toBe('');
    });

    it('should load shopping categories and priorities on init', () => {
        component.ngOnInit();

        expect(mockShoppingReferenceService.getShoppingCategories).toHaveBeenCalled();
        expect(mockShoppingReferenceService.getShoppingPriorities).toHaveBeenCalled();
        expect(mockConfigurationService.getMassUnits).toHaveBeenCalled();
    });

    it('should populate categories and priorities arrays', () => {
        component.ngOnInit();

        expect(component.categories).toEqual(mockCategories);
        expect(component.priorities).toEqual(mockPriorities);
        expect(component.units).toEqual(mockUnits);
    });

    it('should validate required fields', () => {
        const nameControl = component.itemForm.get('name');
        const quantityControl = component.itemForm.get('quantity');

        expect(nameControl?.hasError('required')).toBeTruthy();
        expect(quantityControl?.hasError('required')).toBeFalsy(); // Has default value
    });

    it('should validate name length constraints', () => {
        const nameControl = component.itemForm.get('name');

        nameControl?.setValue('a'); // Too short
        expect(nameControl?.hasError('minlength')).toBeTruthy();

        nameControl?.setValue('a'.repeat(101)); // Too long
        expect(nameControl?.hasError('maxlength')).toBeTruthy();

        nameControl?.setValue('Valid Name'); // Valid length
        expect(nameControl?.errors).toBeNull();
    });

    it('should validate quantity constraints', () => {
        const quantityControl = component.itemForm.get('quantity');

        quantityControl?.setValue(0); // Too low
        expect(quantityControl?.hasError('min')).toBeTruthy();

        quantityControl?.setValue(1001); // Too high
        expect(quantityControl?.hasError('max')).toBeTruthy();

        quantityControl?.setValue(50); // Valid
        expect(quantityControl?.errors).toBeNull();
    });

    it('should emit form submission when valid', () => {
        spyOn(component.formSubmit, 'emit');

        component.itemForm.patchValue({
            name: 'Test Item',
            quantity: 5,
            unit: 'kg',
            categoryId: 1,
            priorityId: 2,
            notes: 'Test notes'
        });

        component.onSubmit();

        expect(component.formSubmit.emit).toHaveBeenCalledWith({
            name: 'Test Item',
            quantity: 5,
            unit: 'kg',
            categoryId: 1,
            priorityId: 2,
            notes: 'Test notes'
        });
    });

    it('should not emit form submission when invalid', () => {
        spyOn(component.formSubmit, 'emit');

        component.itemForm.patchValue({
            name: '', // Invalid - required field empty
            quantity: 5,
            unit: 'kg'
        });

        component.onSubmit();

        expect(component.formSubmit.emit).not.toHaveBeenCalled();
    });

    it('should reset form after successful submission', () => {
        component.itemForm.patchValue({
            name: 'Test Item',
            quantity: 5,
            unit: 'kg',
            categoryId: 1,
            priorityId: 2,
            notes: 'Test notes'
        });

        component.onSubmit();

        expect(component.itemForm.get('name')?.value).toBe('');
        expect(component.itemForm.get('quantity')?.value).toBe(1);
        expect(component.itemForm.get('unit')?.value).toBe('g');
        expect(component.itemForm.get('categoryId')?.value).toBeNull();
        expect(component.itemForm.get('priorityId')?.value).toBeNull();
        expect(component.itemForm.get('notes')?.value).toBe('');
    });

    it('should handle form reset', () => {
        component.itemForm.patchValue({
            name: 'Test Item',
            quantity: 5,
            unit: 'kg'
        });

        component.onReset();

        expect(component.itemForm.get('name')?.value).toBe('');
        expect(component.itemForm.get('quantity')?.value).toBe(1);
        expect(component.itemForm.get('unit')?.value).toBe('g');
    });

    it('should emit cancel event', () => {
        spyOn(component.cancel, 'emit');

        component.onCancel();

        expect(component.cancel.emit).toHaveBeenCalled();
    });
});
