import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApiTokenService } from './api-token.service';

describe('ApiTokenService', () => {
  let service: ApiTokenService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ApiTokenService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the current user\'s tokens', () => {
    service.getTokens().subscribe();
    expect(http.expectOne('/api/User/api-tokens').request.method).toBe('GET');
  });

  it('creates a token with name and description', () => {
    service.createToken({ name: 'ci-bot', description: 'nightly' }).subscribe();
    const req = http.expectOne('/api/User/api-tokens');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'ci-bot', description: 'nightly' });
  });

  it('revokes a token by id', () => {
    service.revokeToken('42').subscribe();
    expect(http.expectOne('/api/User/api-tokens/42').request.method).toBe('DELETE');
  });
});
