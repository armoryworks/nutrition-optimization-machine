import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { Home } from './home.component';

describe('Home (logged out)', () => {
  function create() {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    return fixture;
  }

  const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('explains NOM and offers sign-up before the recipe grids', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;

    const hero = q(el, 'home-hero')!;
    expect(hero).toBeTruthy();
    expect(hero.querySelector('h1')!.textContent).toContain('Meal planning');
    expect(q(el, 'home-create-account-btn')!.getAttribute('href')).toBe('/register');
    expect(q(el, 'home-sign-in-btn')!.getAttribute('href')).toBe('/login');
    expect(q(el, 'home-page')!.firstElementChild).toBe(hero);

    TestBed.inject(HttpTestingController).match(() => true);
  });

  it('keeps the hero while recipes fail to load', () => {
    const fixture = create();
    TestBed.inject(HttpTestingController).match(() => true)
      .forEach(r => r.flush(null, { status: 500, statusText: 'Server Error' }));
    fixture.detectChanges();
    expect(q(fixture.nativeElement, 'home-hero')).toBeTruthy();
  });
});
