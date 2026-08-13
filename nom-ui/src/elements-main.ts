import { provideBrowserGlobalErrorListeners } from '@angular/core';
import { createApplication } from '@angular/platform-browser';
import { createCustomElement } from '@angular/elements';
import { provideHttpClient, withInterceptors, HttpInterceptorFn } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './app/core/interceptors/auth.interceptor';
import { LoginEmbed, APP_ORIGIN } from './app/auth/login-embed.component';

// Entry point for the embeddable login element bundle (built by the
// nom-login-element project; served from the app at /elements/nom-login.js).
// The marketing site loads this script and mounts <nom-login-embed> directly
// in its static page — see nommeal.com src/js/login.js.

// The bundle is served from the app origin, so its own URL tells us where the
// API lives — no configuration needed on the host page.
const appOrigin = new URL(import.meta.url).origin;

// App services call the API with origin-relative URLs (/api/…). On the
// marketing origin those would hit the wrong host; pin them to the app origin.
const absoluteApiInterceptor: HttpInterceptorFn = (req, next) =>
  req.url.startsWith('/') ? next(req.clone({ url: appOrigin + req.url })) : next(req);

createApplication({
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Empty route table: RouterLink (used inside LoginPopover) needs a Router
    // to build hrefs, but all navigation breaks out to the app origin.
    provideRouter([]),
    provideHttpClient(withInterceptors([absoluteApiInterceptor, authInterceptor])),
    provideAnimationsAsync(),
    { provide: APP_ORIGIN, useValue: appOrigin },
  ],
})
  .then((appRef) => {
    if (!customElements.get('nom-login-embed')) {
      customElements.define(
        'nom-login-embed',
        createCustomElement(LoginEmbed, { injector: appRef.injector }),
      );
    }
  })
  .catch((err) => console.error('NOM login element bootstrap failed', err));
