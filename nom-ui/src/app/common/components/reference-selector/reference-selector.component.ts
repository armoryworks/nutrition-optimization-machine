import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl, AbstractControl } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { Observable, Subject } from 'rxjs';
import { takeUntil, startWith, map } from 'rxjs/operators';
import { ReferenceDataService } from '../../services/reference-data.service';
import { ReferenceItemModel } from '../../models/reference-item.model';

@Component({
  selector: 'app-reference-selector',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatSelectModule
  ],
  template: `
    <div class="reference-selector">
      @if (label) {
        <label [for]="controlId" class="reference-selector__label">
          {{ label }}
        </label>
      }
    
      @if (!isMultiSelect) {
        <mat-form-field class="reference-selector__field">
          <mat-label>{{ placeholder || 'Select option' }}</mat-label>
          <mat-select [formControl]="formControl" [id]="controlId">
            @for (item of filteredOptions$ | async; track item) {
              <mat-option [value]="item.referenceId">
                {{ item.referenceName }}
              </mat-option>
            }
          </mat-select>
          @if (control.hasError('required')) {
            <mat-error>
              {{ requiredErrorMessage || 'This field is required' }}
            </mat-error>
          }
        </mat-form-field>
      }
    
      @if (isMultiSelect) {
        <mat-form-field class="reference-selector__field">
          <mat-label>{{ placeholder || 'Select options' }}</mat-label>
          <mat-select [formControl]="formControl" [id]="controlId" multiple>
            @for (item of filteredOptions$ | async; track item) {
              <mat-option [value]="item.referenceId">
                {{ item.referenceName }}
              </mat-option>
            }
          </mat-select>
          @if (control.hasError('required')) {
            <mat-error>
              {{ requiredErrorMessage || 'This field is required' }}
            </mat-error>
          }
        </mat-form-field>
      }
    
      @if (showDescription && selectedItemDescription) {
        <div class="reference-selector__description">
          {{ selectedItemDescription }}
        </div>
      }
    </div>
    `,
  styleUrls: ['./reference-selector.component.scss']
})
export class ReferenceSelectorComponent implements OnInit, OnDestroy {
  @Input() discriminatorId!: number;
  @Input() control!: AbstractControl;
  @Input() label?: string;
  @Input() placeholder?: string;
  @Input() requiredErrorMessage?: string;
  @Input() isMultiSelect = false;
  @Input() showDescription = false;
  @Input() controlId = 'reference-selector';

  @Output() selectionChange = new EventEmitter<ReferenceItemModel | ReferenceItemModel[]>();

  filteredOptions$!: Observable<ReferenceItemModel[]>;
  selectedItemDescription?: string;

  private destroy$ = new Subject<void>();

  // Getter to properly cast AbstractControl to FormControl for template binding
  get formControl(): FormControl {
    return this.control as FormControl;
  }

  constructor(private referenceDataService: ReferenceDataService) { }

  ngOnInit(): void {
    if (!this.discriminatorId) {
      console.error('ReferenceSelectorComponent: discriminatorId is required');
      return;
    }

    if (!this.control) {
      console.error('ReferenceSelectorComponent: control is required');
      return;
    }

    // Get reference data
    this.filteredOptions$ = this.referenceDataService.getReferencesByGroup(this.discriminatorId);

    // Listen for control value changes
    this.control.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(value => {
        this.onSelectionChange(value);
      });

    // Set initial description if value exists
    if (this.control.value) {
      this.updateDescription(this.control.value);
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private onSelectionChange(value: number | number[]): void {
    if (this.showDescription) {
      this.updateDescription(value);
    }

    // Emit selection change
    if (this.isMultiSelect && Array.isArray(value)) {
      const selectedItems = this.getSelectedItems(value);
      this.selectionChange.emit(selectedItems);
    } else if (!this.isMultiSelect && typeof value === 'number') {
      const selectedItem = this.getSelectedItem(value);
      if (selectedItem) {
        this.selectionChange.emit(selectedItem);
      }
    }
  }

  private updateDescription(value: number | number[]): void {
    if (this.isMultiSelect && Array.isArray(value)) {
      const selectedItems = this.getSelectedItems(value);
      this.selectedItemDescription = selectedItems.map(item => item.referenceDescription).join(', ');
    } else if (!this.isMultiSelect && typeof value === 'number') {
      const selectedItem = this.getSelectedItem(value);
      this.selectedItemDescription = selectedItem?.referenceDescription;
    }
  }

  private getSelectedItem(referenceId: number): ReferenceItemModel | undefined {
    let selectedItem: ReferenceItemModel | undefined;
    this.filteredOptions$.pipe(
      takeUntil(this.destroy$),
      map(items => items.find(item => item.referenceId && item.referenceId === referenceId))
    ).subscribe(item => {
      selectedItem = item;
    });
    return selectedItem;
  }

  private getSelectedItems(referenceIds: number[]): ReferenceItemModel[] {
    let items: ReferenceItemModel[] = [];
    this.filteredOptions$.pipe(
      takeUntil(this.destroy$),
      map(options => options.filter(item => item.referenceId && referenceIds.includes(item.referenceId)))
    ).subscribe(filteredItems => {
      items = filteredItems;
    });
    return items;
  }
}
