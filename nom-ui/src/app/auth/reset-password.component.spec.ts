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

    expect(q(fixture.nativeElement, 'reset-password-link-expired')?.textContent).toContain('Link Expired');
    expect(q(fixture.nativeElement, 'reset-password-link-expired')?.textContent).toContain('expired or has already been used');
    expect(fixture.nativeElement.textContent).not.toContain('Choose a new password');
    expect(q(fixture.nativeElement, 'reset-password-request-new-link-btn')?.getAttribute('href')).toBe('/forgot-password');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeNull();
    expect(q(fixture.nativeElement, 'reset-password-success')).toBeNull();
  });

  it('keeps the form and shows the server rule under New Password, not in the banner', () => {
    const fixture = create(link);

    submit(fixture, 'Aa1!Aa1!').flush(
      { errors: { PasswordRequiresUniqueChars: ['Passwords must use at least 6 different characters.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-new-password-error')?.textContent).toContain('more different characters');
    expect(q(fixture.nativeElement, 'reset-password-error')).toBeNull();
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeTruthy();
    expect(q(fixture.nativeElement, 'reset-password-link-expired')).toBeNull();
  });

  it('checks the password rules live and sends nothing until they are met', () => {
    const fixture = create(link);
    const control = fixture.componentInstance.resetForm.controls.newPassword;
    control.setValue('longpassword');
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-password-rule-length')?.classList).toContain('nom-reset__rule--met');
    expect(q(fixture.nativeElement, 'reset-password-password-rule-upper')?.classList).not.toContain('nom-reset__rule--met');

    fixture.componentInstance.resetForm.controls.confirmNewPassword.setValue('longpassword');
    fixture.componentInstance.onSubmit();
    fixture.detectChanges();

    http.expectNone(r => r.url.endsWith('/api/auth/resetPassword'));
    expect(q(fixture.nativeElement, 'reset-password-new-password-error')?.textContent).toContain('rules below');
  });

  it('keeps network failures in the banner above the form', () => {
    const fixture = create(link);

    submit(fixture).flush(null, { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-error')?.textContent).toContain('Unable to reach the server');
    expect(q(fixture.nativeElement, 'reset-password-form')).toBeTruthy();
  });

  it('removes the email and code from the address bar once they are read', () => {
    history.replaceState(null, '', '/reset-password?email=liz%40example.com&code=Q2ZESjhBQUFBQ-_x');
    const fixture = create(link);

    expect(window.location.search).toBe('');
    submit(fixture).flush(null);
  });

  it('offers a single way to sign in after a successful reset', () => {
    const fixture = create(link);

    submit(fixture).flush(null);
    fixture.detectChanges();

    expect(q(fixture.nativeElement, 'reset-password-sign-in-btn')).toBeTruthy();
    expect(q(fixture.nativeElement, 'reset-password-sign-in-link')).toBeNull();
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
