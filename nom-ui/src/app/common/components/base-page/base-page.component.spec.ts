import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { BasePageComponent, BasePageConfig } from './base-page.component';

describe('BasePageComponent', () => {
    let component: BasePageComponent;
    let fixture: ComponentFixture<BasePageComponent>;

    const mockConfig: BasePageConfig = {
        title: 'Test Page',
        subtitle: 'Test Subtitle',
        showBackButton: true,
        backButtonText: 'Back to Test',
        showRefreshButton: true,
        refreshButtonText: 'Refresh Test'
    };

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [
                BasePageComponent,
                NoopAnimationsModule
            ]
        })
            .compileComponents();

        fixture = TestBed.createComponent(BasePageComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should display config values when provided', () => {
        component.config = mockConfig;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Test Page');
        expect(compiled.textContent).toContain('Test Subtitle');
    });

    it('should display custom back button text when provided', () => {
        component.config = { ...mockConfig, showBackButton: true };
        component.backButtonText = 'Custom Back Text';
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Custom Back Text');
    });

    it('should emit back event when back button is clicked', () => {
        spyOn(component.back, 'emit');
        component.config = { ...mockConfig, showBackButton: true };
        fixture.detectChanges();

        const backButton = fixture.nativeElement.querySelector('.back-button');
        backButton.click();

        expect(component.back.emit).toHaveBeenCalled();
    });

    it('should emit refresh event when refresh button is clicked', () => {
        spyOn(component.refresh, 'emit');
        component.config = { ...mockConfig, showRefreshButton: true };
        fixture.detectChanges();

        const refreshButton = fixture.nativeElement.querySelector('.refresh-button');
        refreshButton.click();

        expect(component.refresh.emit).toHaveBeenCalled();
    });

    it('should emit retry event when retry button is clicked', () => {
        spyOn(component.retry, 'emit');
        component.error = 'Test error';
        component.showRetryButton = true;
        fixture.detectChanges();

        const retryButton = fixture.nativeElement.querySelector('button[color="primary"]');
        retryButton.click();

        expect(component.retry.emit).toHaveBeenCalled();
    });

    it('should show error when error is provided', () => {
        component.error = 'Test error message';
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Test error message');
    });

    it('should show loading state when isLoading is true', () => {
        component.isLoading = true;
        fixture.detectChanges();

        const compiled = fixture.nativeElement;
        expect(compiled.textContent).toContain('Loading...');
    });

    it('should log input changes in ngOnChanges', () => {
        spyOn(console, 'log');

        component.ngOnChanges({
            backButtonText: {
                previousValue: 'Old Text',
                currentValue: 'New Text',
                firstChange: false,
                isFirstChange: () => false
            }
        });

        expect(console.log).toHaveBeenCalledWith('BasePageComponent - ngOnChanges:', jasmine.any(Object));
    });

    it('should log input values in ngOnInit', () => {
        spyOn(console, 'log');
        component.backButtonText = 'Test Text';
        component.config = mockConfig;

        component.ngOnInit();

        expect(console.log).toHaveBeenCalledWith('BasePageComponent - ngOnInit - backButtonText:', 'Test Text');
        expect(console.log).toHaveBeenCalledWith('BasePageComponent - ngOnInit - config:', mockConfig);
    });
});
