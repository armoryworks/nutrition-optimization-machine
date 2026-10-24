import { Component, input, output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { App } from './app.component';
import { AuthService } from './core/services/auth.service';
import { NO_CONTEXT_RAIL } from './core/utils/context-rail';

@Component({ selector: 'nom-header', template: '' })
class StubHeader {
  isLoggedIn = input(false);
  isDarkTheme = input(true);
  showPanelToggle = input(true);
  themeToggle = output<void>();
  navToggle = output<void>();
  sidebarToggle = output<void>();
}

@Component({ selector: 'nom-nav', template: '' })
class StubNav {
  navigated = output<void>();
}

@Component({ selector: 'nom-sidebar', template: '' })
class StubSidebar {
  open = input(false);
}

@Component({ selector: 'nom-footer', template: '' })
class StubFooter {}

@Component({ selector: 'nom-loading-overlay', template: '' })
class StubLoadingOverlay {
  visible = input(false);
}

@Component({ template: '' })
class Page {}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });
});

describe('App context rail', () => {
  const loggedIn = signal(true);

  beforeEach(async () => {
    loggedIn.set(true);
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'home', component: Page },
          { path: 'recipe/:id', component: Page, data: NO_CONTEXT_RAIL },
          { path: 'shopping', data: NO_CONTEXT_RAIL, children: [{ path: '', component: Page }] },
        ]),
        provideHttpClient(),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn.asReadonly() } },
      ],
    });
    TestBed.overrideComponent(App, {
      set: {
        imports: [
          RouterOutlet,
          MatIconModule,
          StubHeader,
          StubNav,
          StubSidebar,
          StubFooter,
          StubLoadingOverlay,
        ],
      },
    });
    await TestBed.compileComponents();
  });

  const render = async (url: string) => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  };

  it('shows the rail and its toggle on a page that keeps it', async () => {
    const { el } = await render('/home');
    expect(el.querySelector('[data-testid="context-rail"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="sidebar-toggle-btn"]')).not.toBeNull();
    expect(el.querySelector('.nom-layout')?.classList).toContain('nom-layout--with-sidebar');
  });

  it('drops the rail, its toggle and its grid column on a focused page', async () => {
    const { el } = await render('/recipe/7');
    expect(el.querySelector('[data-testid="context-rail"]')).toBeNull();
    expect(el.querySelector('[data-testid="sidebar-toggle-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="sidebar-backdrop"]')).toBeNull();
    expect(el.querySelector('.nom-layout')?.classList).not.toContain('nom-layout--with-sidebar');
  });

  it('honours the flag on a parent route with lazy children', async () => {
    const { el } = await render('/shopping');
    expect(el.querySelector('[data-testid="context-rail"]')).toBeNull();
  });

  it('closes an open sidecar when moving to a focused page and tells the header to hide its Panel button', async () => {
    const { fixture, el } = await render('/home');
    fixture.componentInstance.toggleSidebar();
    expect(fixture.componentInstance.sidebarOpen()).toBe(true);
    await TestBed.inject(Router).navigateByUrl('/recipe/7');
    fixture.detectChanges();
    expect(fixture.componentInstance.sidebarOpen()).toBe(false);
    expect(fixture.componentInstance.contextRail()).toBe(false);
    expect(el.querySelector('[data-testid="context-rail"]')).toBeNull();
  });

  it('keeps the user’s open sidecar when moving between pages that both show the rail', async () => {
    const { fixture } = await render('/home');
    fixture.componentInstance.toggleSidebar();
    await TestBed.inject(Router).navigateByUrl('/home?tab=2');
    expect(fixture.componentInstance.sidebarOpen()).toBe(true);
  });

  it('shows no rail while signed out', async () => {
    loggedIn.set(false);
    const { el } = await render('/home');
    expect(el.querySelector('[data-testid="context-rail"]')).toBeNull();
  });
});
