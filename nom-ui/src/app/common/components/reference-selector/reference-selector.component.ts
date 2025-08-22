import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { FormControl } from '@angular/forms';
import { Observable, Subject } from 'rxjs';
import { takeUntil, startWith, map } from 'rxjs/operators';
import { ReferenceDataService, ReferenceItem } from '../../services/reference-data.service';

@Component({
    selector: 'app-reference-selector',
    template: `
    <div class="reference-selector">
      <label *ngIf="label" [for]="controlId" class="reference-selector__label">
        {{ label }}
      </label>
      
      <mat-form-field *ngIf="!isMultiSelect" class="reference-selector__field">
        <mat-label>{{ placeholder || 'Select option' }}</mat-label>
        <mat-select [formControl]="control" [id]="controlId">
          <mat-option *ngFor="let item of filteredOptions$ | async" [value]="item.referenceId">
            {{ item.referenceName }}
          </mat-option>
        </mat-select>
        <mat-error *ngIf="control.hasError('required')">
          {{ requiredErrorMessage || 'This field is required' }}
        </mat-error>
      </mat-form-field>

      <mat-form-field *ngIf="isMultiSelect" class="reference-selector__field">
        <mat-label>{{ placeholder || 'Select options' }}</mat-label>
        <mat-select [formControl]="control" [id]="controlId" multiple>
          <mat-option *ngFor="let item of filteredOptions$ | async" [value]="item.referenceId">
            {{ item.referenceName }}
          </mat-option>
        </mat-select>
        <mat-error *ngIf="control.hasError('required')">
          {{ requiredErrorMessage || 'This field is required' }}
        </mat-error>
      </mat-form-field>

      <div *ngIf="showDescription && selectedItemDescription" class="reference-selector__description">
        {{ selectedItemDescription }}
      </div>
    </div>
  `,
    styleUrls: ['./reference-selector.component.scss']
})
export class ReferenceSelectorComponent implements OnInit, OnDestroy {
    @Input() discriminatorId!: number;
    @Input() control!: FormControl;
    @Input() label?: string;
    @Input() placeholder?: string;
    @Input() requiredErrorMessage?: string;
    @Input() isMultiSelect = false;
    @Input() showDescription = false;
    @Input() controlId = 'reference-selector';

    @Output() selectionChange = new EventEmitter<ReferenceItem | ReferenceItem[]>();

    filteredOptions$!: Observable<ReferenceItem[]>;
    selectedItemDescription?: string;

    private destroy$ = new Subject<void>();

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

        if (this.isMultiSelect && Array.isArray(value)) {
            // Multi-select: get all selected items
            this.filteredOptions$.subscribe(options => {
                const selectedItems = options.filter(option => value.includes(option.referenceId));
                this.selectionChange.emit(selectedItems);
            });
        } else if (!this.isMultiSelect && typeof value === 'number') {
            // Single-select: get selected item
            this.filteredOptions$.subscribe(options => {
                const selectedItem = options.find(option => option.referenceId === value);
                if (selectedItem) {
                    this.selectionChange.emit(selectedItem);
                }
            });
        }
    }

    private updateDescription(value: number | number[]): void {
        if (this.isMultiSelect && Array.isArray(value)) {
            // Multi-select: show count of selected items
            this.selectedItemDescription = `${value.length} item(s) selected`;
        } else if (!this.isMultiSelect && typeof value === 'number') {
            // Single-select: show description of selected item
            this.filteredOptions$.subscribe(options => {
                const selectedItem = options.find(option => option.referenceId === value);
                this.selectedItemDescription = selectedItem?.referenceDescription;
            });
        }
    }
}
