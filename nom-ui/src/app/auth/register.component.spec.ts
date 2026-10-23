import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { Register } from './register.component';

describe('Register', () => {
  let http: HttpTestingController;

  function create() {
    TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    return fixture;
  }

  const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  function submit(fixture: ReturnType<typeof create>, password: string) {
    fixture.componentInstance.registerForm.setValue({
      email: 'liz@example.com',
      fullName: '',
      password,
      confirmPassword: password,
    });
    fixture.componentInstance.onSubmit();
    return http.expectOne(r => r.url.endsWith('/register-custom'));
  }

  afterEach(() => http.verify());

  it('shows the mismatch under Confirm Password and sends nothing when the passwords differ', () => {
    const fixture = create();
    fixture.componentInstance.registerForm.setValue({
      email: 'liz@example.com',
      fullName: '',
      password: 'Abcdefg1!',
      confirmPassword: 'Abcdefg1?',
    });
    fixture.componentInstance.onSubmit();
    fixture.detectChanges();

    http.expectNone(r => r.url.endsWith('/register-custom'));
    expect(fixture.componentInstance.registerForm.controls.confirmPassword.hasError('passwordMismatch')).toBe(true);
    expect(q(fixture.nativeElement, 'register-confirm-password-error')?.textContent).toContain('Passwords do not match');

    fixture.componentInstance.registerForm.controls.password.setValue('Abcdefg1?');
    expect(fixture.componentInstance.registerForm.valid).toBe(true);
  });

  it('shows the password rules before the user submits', () => {
    const fixture = create();
    const rules = q(fixture.nativeElement, 'register-password-rules')!.textContent!;
    expect(rules).toContain('At least 8 characters');
    expect(rules).toContain('An uppercase letter');
    expect(rules).toContain('A lowercase letter');
    expect(rules).toContain('A number');
    expect(rules).toContain('A symbol');
  });

  it('ticks rules off as the password is typed', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.registerForm.controls.password.setValue('Abcdefgh12345678');
    fixture.detectChanges();
    expect(q(el, 'register-password-rule-length')!.classList).toContain('nom-register__rule--met');
    expect(q(el, 'register-password-rule-symbol')!.classList).not.toContain('nom-register__rule--met');
    expect(fixture.componentInstance.registerForm.controls.password.hasError('passwordRules')).toBe(true);
  });

  it('shows the specific message for each code in the API\'s error array', () => {
    const fixture = create();
    submit(fixture, 'Abcdefg1!').flush(
      {
        message: 'Registration failed.',
        errors: [
          { code: 'PasswordRequiresNonAlphanumeric', description: 'Passwords must have at least one non alphanumeric character.' },
          { code: 'PasswordRequiresUpper', description: 'Passwords must have at least one uppercase (\'A\'-\'Z\').' },
        ],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    const error = q(fixture.nativeElement, 'register-error')!.textContent!;
    expect(error).toContain('Password must contain at least one special character.');
    expect(error).toContain('Password must contain at least one uppercase letter.');
    expect(error).not.toContain('Please check your details');
  });

  it('still understands the validation-problem object shape', () => {
    const fixture = create();
    submit(fixture, 'Abcdefg1!').flush(
      { errors: { DuplicateUserName: ['taken'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(q(fixture.nativeElement, 'register-error')!.textContent)
      .toContain('An account with this email already exists.');
  });

  it('says the account exists when the API reports a duplicate without codes', () => {
    const fixture = create();
    submit(fixture, 'Abcdefg1!').flush(
      { message: 'User with this email already exists.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(q(fixture.nativeElement, 'register-error')!.textContent)
      .toContain('An account with this email already exists.');
  });
});
