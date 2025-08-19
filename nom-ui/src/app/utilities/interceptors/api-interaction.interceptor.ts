import { Injectable, inject } from '@angular/core';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { NomConfigService } from '../services/nom-config.service';

@Injectable()
export class ApiInteractionInterceptor implements HttpInterceptor {
  private configService = inject(NomConfigService);



  intercept(
    req: HttpRequest<unknown>,
    next: HttpHandler
  ): Observable<HttpEvent<unknown>> {
    if (
      this.configService?.config?.serverUri &&
      !req.url.startsWith('http://') &&
      !req.url.startsWith('https://') &&
      req.url.startsWith('/api/')
    ) {
      const modifiedReq = req.clone({
        url: `${this.configService.config.serverUri}${req.url}`,
      });
      return next.handle(modifiedReq);
    }
    return next.handle(req);
  }
}
