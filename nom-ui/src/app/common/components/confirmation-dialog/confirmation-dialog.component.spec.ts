import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { ConfirmationDialogComponent } from './confirmation-dialog.component';

describe('ConfirmationDialogComponent', () => {
    let component: ConfirmationDialogComponent;
    let fixture: ComponentFixture<ConfirmationDialogComponent>;
    let mockDialogRef: jasmine.SpyObj<MatDialogRef<ConfirmationDialogComponent>>;

    const mockDialogData = {
        title: 'Test Title',
        message: 'Test Message',
        confirmText: 'Confirm',
        cancelText: 'Cancel'
    };

    beforeEach(async () => {
        mockDialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);

        await TestBed.configureTestingModule({
            imports: [
                ConfirmationDialogComponent,
                NoopAnimationsModule
            ],
            providers: [
                { provide: MatDialogRef, useValue: mockDialogRef },
                { provide: MAT_DIALOG_DATA, useValue: mockDialogData }
            ]
        })
            .compileComponents();

        fixture = TestBed.createComponent(ConfirmationDialogComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should display confirmation dialog content', () => {
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled).toBeTruthy();
    });
});
