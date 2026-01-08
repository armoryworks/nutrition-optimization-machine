import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';
import { ReferenceItem } from '../../../common/services/reference-data.service';

@Component({
    selector: 'app-shopping-item-form',
    template: `
    <div class="shopping-item-form">
      <h3>Add Shopping Item</h3>
    
      <form [formGroup]="shoppingItemForm" (ngSubmit)="onSubmit()">
        <div class="form-row">
          <mat-form-field class="form-field">
            <mat-label>Item Name</mat-label>
            <input matInput formControlName="name" placeholder="Enter item name">
            @if (shoppingItemForm.get('name')?.hasError('required')) {
              <mat-error>
                Item name is required
              </mat-error>
            }
          </mat-form-field>
        </div>
    
        <div class="form-row">
          <app-reference-selector
            [discriminatorId]="REFERENCE_IDS.SHOPPING_CATEGORY_TYPE"
            [control]="shoppingItemForm.get('categoryId')!"
            label="Category"
            placeholder="Select category"
            [showDescription]="true"
            (selectionChange)="onCategoryChange($event)">
          </app-reference-selector>
        </div>
    
        <div class="form-row">
          <app-reference-selector
            [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
            [control]="shoppingItemForm.get('priorityId')!"
            label="Priority"
            placeholder="Select priority"
            [showDescription]="true"
            (selectionChange)="onPriorityChange($event)">
          </app-reference-selector>
        </div>
    
        <div class="form-row">
          <mat-form-field class="form-field">
            <mat-label>Quantity</mat-label>
            <input matInput type="number" formControlName="quantity" min="1">
            @if (shoppingItemForm.get('quantity')?.hasError('required')) {
              <mat-error>
                Quantity is required
              </mat-error>
            }
            @if (shoppingItemForm.get('quantity')?.hasError('min')) {
              <mat-error>
                Quantity must be at least 1
              </mat-error>
            }
          </mat-form-field>
        </div>
    
        <div class="form-actions">
          <button mat-button type="button" (click)="onCancel()">Cancel</button>
          <button mat-raised-button color="primary" type="submit" [disabled]="shoppingItemForm.invalid">
            Add Item
          </button>
        </div>
      </form>
    
      @if (selectedCategory()) {
        <div class="selected-info">
          <strong>Selected Category:</strong> {{ selectedCategory()!.referenceName }}
          <br>
            <em>{{ selectedCategory()!.referenceDescription }}</em>
          </div>
        }

        @if (selectedPriority()) {
          <div class="selected-info">
            <strong>Selected Priority:</strong> {{ selectedPriority()!.referenceName }}
            <br>
              <em>{{ selectedPriority()!.referenceDescription }}</em>
            </div>
          }
        </div>
    `,
    styleUrls: ['./shopping-item-form.component.scss']
})
export class ShoppingItemFormComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private shoppingReferenceService = inject(ShoppingReferenceService);

    shoppingItemForm: FormGroup;
    selectedCategory = signal<ReferenceItem | undefined>(undefined);
    selectedPriority = signal<ReferenceItem | undefined>(undefined);

    // Make constants available in template
    readonly REFERENCE_IDS = REFERENCE_IDS;

    private destroy$ = new Subject<void>();

    constructor() {
        this.shoppingItemForm = this.fb.group({
            name: ['', Validators.required],
            categoryId: [null, Validators.required],
            priorityId: [null, Validators.required],
            quantity: [1, [Validators.required, Validators.min(1)]]
        });
    }

    ngOnInit(): void {
        // Preload shopping references for better performance
        this.shoppingReferenceService.getShoppingReferencesBulk()
            .pipe(takeUntil(this.destroy$))
            .subscribe();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onCategoryChange(category: ReferenceItem): void {
        this.selectedCategory.set(category);
        console.log('Category selected:', category);
    }

    onPriorityChange(priority: ReferenceItem): void {
        this.selectedPriority.set(priority);
        console.log('Priority selected:', priority);
    }

    onSubmit(): void {
        if (this.shoppingItemForm.valid) {
            const formValue = this.shoppingItemForm.value;
            console.log('Form submitted:', formValue);

            // Here you would typically send the data to your backend
            // For now, we'll just log it
            alert(`Shopping item added: ${formValue.name} (Category: ${this.selectedCategory()?.referenceName}, Priority: ${this.selectedPriority()?.referenceName})`);

            // Reset form
            this.shoppingItemForm.reset({ quantity: 1 });
            this.selectedCategory.set(undefined);
            this.selectedPriority.set(undefined);
        }
    }

    onCancel(): void {
        this.shoppingItemForm.reset({ quantity: 1 });
        this.selectedCategory.set(undefined);
        this.selectedPriority.set(undefined);
    }
}
