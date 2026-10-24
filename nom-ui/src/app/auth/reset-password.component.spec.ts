import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ResetPassword } from './reset-password.component';

describe('ResetPassword', () => {
  let http: HttpTestingController;

  function create(queryParams: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ResetPassword);
    fixture.detectChanges();
    return fixture;
  }

  const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const link = { email: 'liz@example.com', code: 'Q2ZESjhBQUFBQ-_x' };

  function submit(fixture: ReturnType<typeof create>, password = 'NewPassword1!') {
    fixture.componentInstance.resetForm.setValue({ newPassword: password, confirmNewPassword: password });
    fixture.componentInstance.onSubmit();
    return http.expectOne(r => r.url.endsWith('/api/auth/resetPassword'));
  }

  afterEach(() => http.verify());

  it('shows the no-token state when the link has no code', () => {
    const fixture = create({ email: 'liz@example.com' });

    expect(q(fixture.nativeElement, 'reset-password-invalid-link')).toBeTruthy();
    expect(q(fixture.nativeElement, 'reset-password-request-link-btn')?.getAttribute('href')).toBe('/forgot-password');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeNull();
  });

  it('shows the no-token state when the link has no email', () => {
    const fixture = create({ code: link.code });

    expect(q(fixture.nativeElement, 'reset-password-invalid-link')).toBeTruthy();
  });

  it('sends the email, the code from the link and the new password exactly as the API expects', () => {
    const fixture = create(link);

    const req = submit(fixture);

    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: link.email, resetCode: link.code, newPassword: 'NewPassword1!' });
    req.flush(null);
  });

  it('says the password was reset and offers sign-in when the API accepts it', () => {
    const fixture = create(link);

    submit(fixture).flush(null);
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-success')?.textContent).toContain('Your password has been changed');
    expect(q(fixture.nativeElement, 'reset-password-sign-in-btn')?.getAttribute('href')).toBe('/login');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeNull();
  });

  it('explains an expired or used link and offers a new one', () => {
    const fixture = create(link);

    submit(fixture).flush(
      { title: 'One or more validation errors occurred.', errors: { InvalidToken: ['Invalid token.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-error')?.textContent).toContain('expired or has already been used');
    expect(q(fixture.nativeElement, 'reset-password-request-new-link-btn')?.getAttribute('href')).toBe('/forgot-password');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeNull();
    expect(q(fixture.nativeElement, 'reset-password-success')).toBeNull();
  });

  it('keeps the form and names the rule when the server rejects the password itself', () => {
    const fixture = create(link);

    submit(fixture, 'newpassword1').flush(
      { errors: { PasswordRequiresUpper: ['Passwords must have at least one uppercase.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-error')?.textContent).toContain('uppercase letter');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeTruthy();
    expect(q(fixture.nativeElement, 'reset-password-request-new-link-btn')).toBeNull();
  });

  it('shows the mismatch under Confirm New Password and sends nothing', () => {
    const fixture = create(link);
    fixture.componentInstance.resetForm.setValue({ newPassword: 'NewPassword1!', confirmNewPassword: 'NewPassword1?' });
    fixture.componentInstance.onSubmit();
    fixture.detectChanges();

    http.expectNone(r => r.url.endsWith('/api/auth/resetPassword'));
    expect(q(fixture.nativeElement, 'reset-password-confirm-password-error')?.textContent).toContain('Passwords do not match');
  });
});
