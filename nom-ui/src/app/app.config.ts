import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import {
  provideClientHydration,
  withEventReplay,
} from '@angular/platform-browser';
import {
  HTTP_INTERCEPTORS,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';

import { BearerInterceptor } from './utilities/interceptors/bearer-interceptor';
import { ApiInteractionInterceptor } from './utilities/interceptors/api-interaction-interceptor';
import { routes } from './app.routes';
import { NomConfigService } from './utilities/services/nom-config.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideClientHydration(withEventReplay()),
    provideHttpClient(withInterceptorsFromDi()),
    {
      provide: NomConfigService,
      useClass: NomConfigService,
    },
    {
      provide: BearerInterceptor,
      useClass: BearerInterceptor,
    },
    {
      provide: ApiInteractionInterceptor,
      useClass: ApiInteractionInterceptor,
    },
    {
      provide: HTTP_INTERCEPTORS,
      useExisting: BearerInterceptor,
      multi: true,
    },
    {
      provide: HTTP_INTERCEPTORS,
      useExisting: ApiInteractionInterceptor,
      multi: true,
    },
  ],
};
