import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import { RegisterUser } from './models/register-user';
import { LoginUser } from './models/login-user';
import { ForgotPassword } from './models/forgot-password';
import { ResetPassword } from './models/reset-password';
import { ConfirmEmail } from './models/confirm-email';
import { SendConfirmationEmail } from './models/send-confirmation-email';
import { UpdateInfo } from './models/update-info';
import { UpdateTwoFactor } from './models/update-two-factor';
import { LoginResponse } from './models/login-response';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  constructor(private httpClient: HttpClient) {}

  /**
   * Placeholder for user registration.
   * @param userData User registration details adhering to RegisterUser interface.
   * @returns Observable of any.
   */
  register(userData: RegisterUser): Observable<any> {
    console.log('Registering user:', userData);
    // Simulate API call
    return of({
      success: true,
      message: 'Registration successful. Please verify your email.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock registration complete.'))
    );
  }

  /**
   * Placeholder for user login.
   * @param credentials User login credentials adhering to LoginUser interface.
   * @returns Observable of any.
   */
  login(
    credentials: LoginUser,
    useCookies: boolean = false,
    useSessionCookies: boolean = false
  ): Observable<LoginResponse> {
    return this.httpClient.post<LoginResponse>('/api/auth/login', credentials);
  }

  logout(): Observable<void> {
    return this.httpClient.post<void>('/api/auth/logout', undefined);
  }

  /**
   * Placeholder for forgot password request.
   * @param data Forgot password request data adhering to ForgotPassword interface.
   * @returns Observable of any.
   */
  forgotPassword(data: ForgotPassword): Observable<any> {
    console.log('Forgot password request for:', data.email);
    // Simulate API call
    return of({
      success: true,
      message: 'Password reset link sent to your email.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock forgot password complete.'))
    );
  }

  /**
   * Placeholder for password reset.
   * @param resetData Password reset details adhering to ResetPassword interface.
   * @returns Observable of any.
   */
  resetPassword(resetData: ResetPassword): Observable<any> {
    console.log('Resetting password:', resetData);
    // Simulate API call
    return of({
      success: true,
      message: 'Your password has been reset successfully.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock password reset complete.'))
    );
  }

  /**
   * Placeholder for sending confirmation email.
   * @param data Email data adhering to SendConfirmationEmail interface.
   * @returns Observable of any.
   */
  sendConfirmationEmail(data: SendConfirmationEmail): Observable<any> {
    console.log('Sending confirmation email to:', data.email);
    // Simulate API call
    return of({
      success: true,
      message: 'Confirmation email sent. Please check your inbox.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock send email complete.'))
    );
  }

  /**
   * Placeholder for updating account information (email and/or password).
   * @param updateData UpdateInfo data.
   * @returns Observable of any.
   */
  updateInfo(updateData: UpdateInfo): Observable<any> {
    console.log('Updating account info:', updateData);
    // Simulate API call
    return of({
      success: true,
      message: 'Account information updated successfully.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock account info update complete.'))
    );
  }

  /**
   * Placeholder for two-factor authentication management.
   * @param data UpdateTwoFactor data.
   * @returns Observable of any.
   */
  updateTwoFactorAuth(data: UpdateTwoFactor): Observable<any> {
    console.log('Updating 2FA settings:', data);
    // Simulate API call
    return of({
      success: true,
      message: 'Two-Factor Authentication settings updated!',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock 2FA update complete.'))
    );
  }

  /**
   * Placeholder for confirming email, typically via querystring parameters.
   * @param data ConfirmEmail data (userId, code, changedEmail).
   * @returns Observable of any.
   */
  confirmEmail(data: ConfirmEmail): Observable<any> {
    console.log('Confirming email with data:', data);
    // Simulate API call for confirming email
    return of({ success: true, message: 'Email confirmed successfully!' }).pipe(
      delay(1000),
      tap(() => console.log('Mock email confirmation complete.'))
    );
  }
}
