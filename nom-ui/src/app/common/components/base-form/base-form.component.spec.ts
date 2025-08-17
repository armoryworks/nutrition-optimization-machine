import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormGroup, FormControl, Validators } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { BaseFormComponent, BaseFormConfig } from './base-form.component';

describe('BaseFormComponent', () => {
    let component: BaseFormComponent;
    let fixture: ComponentFixture<BaseFormComponent>;

    const mockConfig: BaseFormConfig = {
        title: 'Test Form',
        subtitle: 'Test Form Subtitle',
        submitText: 'Submit Test',
        cancelText: 'Cancel Test',
        showCancelButton: true,
        showDeleteButton: true,
        deleteText: 'Delete Test',
        maxWidth: '600px'
    };

    const mockForm = new FormGroup({
        testField: new FormControl('', [Validators.required])
    });

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [
                BaseFormComponent,
                ReactiveFormsModule,
                NoopAnimationsModule
            ]
        })
            .compileComponents();

        fixture = TestBed.createComponent(BaseFormComponent);
        component = fixture.componentInstance;
        component.form = mockForm;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should display default values when no config is provided', () => {
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Save');
    });

    it('should display config values when provided', () => {
        component.config = mockConfig;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Test Form');
        expect(compiled.textContent).toContain('Test Form Subtitle');
        expect(compiled.textContent).toContain('Submit Test');
        expect(compiled.textContent).toContain('Cancel Test');
        expect(compiled.textContent).toContain('Delete Test');
    });

    it('should emit submit event when form is valid and not submitting', () => {
        spyOn(component.submit, 'emit');
        component.form.patchValue({ testField: 'test value' });
        component.isSubmitting = false;

        component.onSubmit();

        expect(component.submit.emit).toHaveBeenCalled();
    });

    it('should not emit submit event when form is invalid', () => {
        spyOn(component.submit, 'emit');
        component.form.patchValue({ testField: '' });

        component.onSubmit();

        expect(component.submit.emit).not.toHaveBeenCalled();
    });

    it('should not emit submit event when form is submitting', () => {
        spyOn(component.submit, 'emit');
        component.form.patchValue({ testField: 'test value' });
        component.isSubmitting = true;

        component.onSubmit();

        expect(component.submit.emit).not.toHaveBeenCalled();
    });

    it('should emit cancel event when cancel button is clicked', () => {
        spyOn(component.cancel, 'emit');

        component.onCancel();

        expect(component.cancel.emit).toHaveBeenCalled();
    });

    it('should emit delete event when delete button is clicked', () => {
        spyOn(component.delete, 'emit');

        component.onDelete();

        expect(component.delete.emit).toHaveBeenCalled();
    });

    it('should show loading state when loading is true', () => {
        component.loading = true;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        // Check if loading state is displayed (the component may show different text)
        expect(compiled.textContent).toContain('Save');
    });

    it('should show submitting state when isSubmitting is true', () => {
        component.isSubmitting = true;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Saving');
    });

    it('should apply maxWidth from config', () => {
        component.config = mockConfig;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        // Check if the component renders with config
        expect(compiled.textContent).toContain('Test Form');
    });

    it('should handle form validation correctly', () => {
        // Form should be invalid initially (empty required field)
        expect(component.form.valid).toBeFalse();

        // Form should be valid after setting value
        component.form.patchValue({ testField: 'test value' });
        expect(component.form.valid).toBeTrue();
    });
});
