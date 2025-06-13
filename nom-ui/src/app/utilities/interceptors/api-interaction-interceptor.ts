import { Injectable } from '@angular/core';
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
  constructor(private configService: NomConfigService) {}

  intercept(
    req: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    if (
      this.configService?.config?.serverUri &&
      !req.url.startsWith('http://') && // Ensure it's not already an absolute URL
      !req.url.startsWith('https://') &&
      req.url.startsWith('/api/')
    ) {
      const modifiedReq = req.clone({
        url: this.configService.config.serverUri + req.url,
      });
      return next.handle(modifiedReq);
    }
    return next.handle(req);
  }
}
