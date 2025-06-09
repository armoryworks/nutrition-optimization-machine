import { Injectable } from "@angular/core";
import { Observable, of } from "rxjs";
import { delay, tap } from "rxjs/operators";
import { RegisterUser } from "./models/register-user";
import { LoginUser } from "./models/login-user";
import { ForgotPassword } from "./models/forgot-password";
import { ResetPassword } from "./models/reset-password";
import { ConfirmEmail } from "./models/confirm-email";
import { SendConfirmationEmail } from "./models/send-confirmation-email";
import { UpdateInfo } from "./models/update-info";
import { UpdateTwoFactor } from "./models/update-two-factor";
import { LoginResponse } from "./models/auth-response";

interface AuthResponse {
  success: boolean;
  message: string;
  token?: string;
  userId?: string;
}

@Injectable({
  providedIn: "root",
})
export class AuthService {
  constructor() {}

  /**
   * Placeholder for user registration.
   * @param userData User registration details adhering to RegisterUser interface.
   * @returns Observable of AuthResponse.
   */
  register(userData: RegisterUser): Observable<AuthResponse> {
    console.log("Registering user:", userData);
    // Simulate API call
    return of({
      success: true,
      message: "Registration successful. Please verify your email.",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock registration complete."))
    );
  }

  /**
   * Placeholder for user login.
   * @param credentials User login credentials adhering to LoginUser interface.
   * @returns Observable of AuthResponse.
   */
  login(
    credentials: LoginUser,
    useCookies: boolean = false,
    useSessionCookies: boolean = false
  ): Observable<LoginResponse> {
    console.log("Logging in user:", credentials);
    // Simulate API call (basic success/failure logic)
    if (
      credentials.email === "test@example.com" &&
      credentials.password === "password123"
    ) {
      return of({
        tokenType: "bearer?",
        accessToken: "test-token",
        expiresIn: 5000000,
        refreshToken: "test-token",
      }).pipe(
        delay(1000),
        tap(() => console.log("Mock login complete."))
      );
    } else {
      return of({
        tokenType: "bearer?",
        accessToken: "test-token",
        expiresIn: 5000000,
        refreshToken: "test-token",
      }).pipe(
        delay(1000),
        tap(() => console.log("Mock login complete."))
      );
    }
  }

  /**
   * Placeholder for forgot password request.
   * @param data Forgot password request data adhering to ForgotPassword interface.
   * @returns Observable of AuthResponse.
   */
  forgotPassword(data: ForgotPassword): Observable<AuthResponse> {
    console.log("Forgot password request for:", data.email);
    // Simulate API call
    return of({
      success: true,
      message: "Password reset link sent to your email.",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock forgot password complete."))
    );
  }

  /**
   * Placeholder for password reset.
   * @param resetData Password reset details adhering to ResetPassword interface.
   * @returns Observable of AuthResponse.
   */
  resetPassword(resetData: ResetPassword): Observable<AuthResponse> {
    console.log("Resetting password:", resetData);
    // Simulate API call
    return of({
      success: true,
      message: "Your password has been reset successfully.",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock password reset complete."))
    );
  }

  /**
   * Placeholder for sending confirmation email.
   * @param data Email data adhering to SendConfirmationEmail interface.
   * @returns Observable of AuthResponse.
   */
  sendConfirmationEmail(data: SendConfirmationEmail): Observable<AuthResponse> {
    console.log("Sending confirmation email to:", data.email);
    // Simulate API call
    return of({
      success: true,
      message: "Confirmation email sent. Please check your inbox.",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock send email complete."))
    );
  }

  /**
   * Placeholder for updating account information (email and/or password).
   * @param updateData UpdateInfo data.
   * @returns Observable of AuthResponse.
   */
  updateInfo(updateData: UpdateInfo): Observable<AuthResponse> {
    console.log("Updating account info:", updateData);
    // Simulate API call
    return of({
      success: true,
      message: "Account information updated successfully.",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock account info update complete."))
    );
  }

  /**
   * Placeholder for two-factor authentication management.
   * @param data UpdateTwoFactor data.
   * @returns Observable of AuthResponse.
   */
  updateTwoFactorAuth(data: UpdateTwoFactor): Observable<AuthResponse> {
    console.log("Updating 2FA settings:", data);
    // Simulate API call
    return of({
      success: true,
      message: "Two-Factor Authentication settings updated!",
    }).pipe(
      delay(1000),
      tap(() => console.log("Mock 2FA update complete."))
    );
  }

  /**
   * Placeholder for confirming email, typically via querystring parameters.
   * @param data ConfirmEmail data (userId, code, changedEmail).
   * @returns Observable of AuthResponse.
   */
  confirmEmail(data: ConfirmEmail): Observable<AuthResponse> {
    console.log("Confirming email with data:", data);
    // Simulate API call for confirming email
    return of({ success: true, message: "Email confirmed successfully!" }).pipe(
      delay(1000),
      tap(() => console.log("Mock email confirmation complete."))
    );
  }
}
