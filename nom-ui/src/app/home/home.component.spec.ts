import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  let component: HomeComponent;
  let fixture: ComponentFixture<HomeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        HomeComponent,
        NoopAnimationsModule
      ],
      providers: [provideRouter([])]
    })
      .compileComponents();

    fixture = TestBed.createComponent(HomeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should display the main title', () => {
    const compiled = fixture.nativeElement;
    expect(compiled.querySelector('.home__hero-title')).toBeTruthy();
    expect(compiled.textContent).toContain('Welcome to NOM');
  });

  it('should have navigation links', () => {
    const compiled = fixture.nativeElement;
    const buttons = compiled.querySelectorAll('button[routerLink]');
    expect(buttons.length).toBeGreaterThan(0);
  });
});
