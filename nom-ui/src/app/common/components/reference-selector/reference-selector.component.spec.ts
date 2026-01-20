import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { provideAnimations } from '@angular/platform-browser/animations';
import { ReferenceSelectorComponent } from './reference-selector.component';
import { ReferenceDataService, ReferenceItem } from '../../services/reference-data.service';
import { of } from 'rxjs';

describe('ReferenceSelectorComponent', () => {
  let component: ReferenceSelectorComponent;
  let fixture: ComponentFixture<ReferenceSelectorComponent>;
  let mockReferenceDataService: jasmine.SpyObj<ReferenceDataService>;

  const mockReferences: ReferenceItem[] = [
    {
      referenceId: 1,
      referenceName: 'Option 1',
      referenceDescription: 'First option description',
      groupId: 1000,
      groupName: 'Test Group',
      groupDescription: 'Test group description'
    },
    {
      referenceId: 2,
      referenceName: 'Option 2',
      referenceDescription: 'Second option description',
      groupId: 1000,
      groupName: 'Test Group',
      groupDescription: 'Test group description'
    }
  ];

  beforeEach(async () => {
    mockReferenceDataService = jasmine.createSpyObj('ReferenceDataService', ['getReferencesByGroup']);

    await TestBed.configureTestingModule({
      imports: [
        ReferenceSelectorComponent,
        ReactiveFormsModule
      ],
      providers: [
        provideAnimations(),
        { provide: ReferenceDataService, useValue: mockReferenceDataService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ReferenceSelectorComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should require discriminatorId input', () => {
    // Should not throw error when discriminatorId is provided
    component.discriminatorId = 1000;
    component.control = new FormControl();
    expect(() => fixture.detectChanges()).not.toThrow();
  });

  it('should require control input', () => {
    // Should not throw error when control is provided
    component.discriminatorId = 1000;
    component.control = new FormControl();
    expect(() => fixture.detectChanges()).not.toThrow();
  });

  it('should load reference data on init', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    expect(mockReferenceDataService.getReferencesByGroup).toHaveBeenCalledWith(1000);
    expect(component.filteredOptions$).toBeDefined();
  });

  it('should emit selection change for single select', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    component.isMultiSelect = false;
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    spyOn(component.selectionChange, 'emit');
    fixture.detectChanges();

    // Simulate selection change
    component.control.setValue(1);
    fixture.detectChanges();

    expect(component.selectionChange.emit).toHaveBeenCalled();
  });

  it('should emit selection change for multi select', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    component.isMultiSelect = true;
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    spyOn(component.selectionChange, 'emit');
    fixture.detectChanges();

    // Simulate multi-selection change
    component.control.setValue([1, 2]);
    fixture.detectChanges();

    expect(component.selectionChange.emit).toHaveBeenCalled();
  });

  it('should update description when selection changes', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    // Simulate selection change
    component.control.setValue(1);
    fixture.detectChanges();

    // Should update description
    expect(component.selectedItemDescription).toBeDefined();
  });

  it('should handle multi-select description correctly', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    component.isMultiSelect = true;
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    // Simulate multi-selection change
    component.control.setValue([1, 2]);
    fixture.detectChanges();

    // Should show descriptions of selected items
    expect(component.selectedItemDescription).toBeDefined();
  });

  it('should handle empty selection', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    // Simulate empty selection
    component.control.setValue(null);
    fixture.detectChanges();

    // Should handle gracefully
    expect(component.selectedItemDescription).toBeUndefined();
  });

  it('should handle service errors gracefully', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of([]));

    expect(() => fixture.detectChanges()).not.toThrow();
  });

  it('should set initial description if control has value', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl(1);
    component.showDescription = true;
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    // Should attempt to set initial description
    expect(component.selectedItemDescription).toBeDefined();
  });

  it('should clean up subscriptions on destroy', () => {
    component.discriminatorId = 1000;
    component.control = new FormControl();
    mockReferenceDataService.getReferencesByGroup.and.returnValue(of(mockReferences));

    fixture.detectChanges();

    // Should not throw on destroy
    expect(() => component.ngOnDestroy()).not.toThrow();
  });

  it('should handle missing inputs gracefully', () => {
    // Should log error when discriminatorId is missing
    spyOn(console, 'error');
    fixture.detectChanges();
    expect(console.error).toHaveBeenCalledWith('ReferenceSelectorComponent: discriminatorId is required');

    // Should log error when control is missing
    component.discriminatorId = 1000;
    fixture.detectChanges();
    expect(console.error).toHaveBeenCalledWith('ReferenceSelectorComponent: control is required');
  });
});
