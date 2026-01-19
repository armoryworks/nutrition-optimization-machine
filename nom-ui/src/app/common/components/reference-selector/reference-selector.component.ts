import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { ReactiveFormsModule, FormControl, AbstractControl } from '@angular/forms';
import { AmwSelectComponent } from 'angular-material-wrap';
import { Observable, Subject } from 'rxjs';
import { takeUntil, startWith, map } from 'rxjs/operators';
import { ReferenceDataService } from '../../services/reference-data.service';
import { ReferenceItemModel } from '../../models/reference-item.model';

@Component({
  selector: 'app-reference-selector',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwSelectComponent,
  ],
  template: `
    <div class="reference-selector">
      @if (label) {
        <label [for]="controlId" class="reference-selector__label">
          {{ label }}
        </label>
      }

      <amw-select
        [formControl]="formControl"
        [label]="placeholder || (isMultiSelect ? 'Select options' : 'Select option')"
        [options]="getSelectOptions()"
        [multiple]="isMultiSelect"
        [required]="control.hasError('required')"
        class="reference-selector__field">
      </amw-select>

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

  // Helper method for AMW select options
  private cachedOptions: { value: number; label: string }[] = [];

  getSelectOptions(): { value: number; label: string }[] {
    this.filteredOptions$?.pipe(takeUntil(this.destroy$)).subscribe(items => {
      this.cachedOptions = items.map(item => ({
        value: item.referenceId!,
        label: item.referenceName || ''
      }));
    });
    return this.cachedOptions;
  }
}
