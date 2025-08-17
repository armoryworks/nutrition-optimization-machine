import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { RegistrationComponent } from './registration.component';
import { AuthService } from '../auth.service';
import { AuthManagerService } from '../../utilities/services/auth-manager.service';
import { NotificationService } from '../../utilities/services/notification.service';
import { RegisterUser } from '../models/register-user';
import { LoginUser } from '../models/login-user';
import { LoginResponse } from '../models/login-response';

describe('RegistrationComponent', () => {
    let component: RegistrationComponent;
    let fixture: ComponentFixture<RegistrationComponent>;
    let authService: jasmine.SpyObj<AuthService>;
    let authManagerService: jasmine.SpyObj<AuthManagerService>;
    let notificationService: jasmine.SpyObj<NotificationService>;

    beforeEach(async () => {
        const authServiceSpy = jasmine.createSpyObj('AuthService', ['register', 'login']);
        const authManagerServiceSpy = jasmine.createSpyObj('AuthManagerService', ['login'], {
            rememberMe: true
        });
        const notificationServiceSpy = jasmine.createSpyObj('NotificationService', ['success', 'error', 'warning']);

        await TestBed.configureTestingModule({
            imports: [
                RegistrationComponent,
                ReactiveFormsModule,
                HttpClientTestingModule,
                RouterTestingModule,
                NoopAnimationsModule
            ],
            providers: [
                { provide: AuthService, useValue: authServiceSpy },
                { provide: AuthManagerService, useValue: authManagerServiceSpy },
                { provide: NotificationService, useValue: notificationServiceSpy }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(RegistrationComponent);
        component = fixture.componentInstance;
        authService = TestBed.inject(AuthService) as jasmine.SpyObj<AuthService>;
        authManagerService = TestBed.inject(AuthManagerService) as jasmine.SpyObj<AuthManagerService>;
        notificationService = TestBed.inject(NotificationService) as jasmine.SpyObj<NotificationService>;

        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should auto-login after successful registration', () => {
        // Arrange
        const registerUser: RegisterUser = {
            email: 'test@example.com',
            password: 'password123',
            confirmPassword: 'password123',
            fullName: 'Test User'
        };

        const loginCredentials: LoginUser = {
            email: 'test@example.com',
            password: 'password123',
            twoFactorCode: '',
            toFactorRecoveryCode: '',
            rememberMe: true
        };

        const loginResponse: LoginResponse = {
            tokenType: 'Bearer',
            accessToken: 'test-access-token',
            refreshToken: 'test-refresh-token',
            expiresIn: 3600
        };

        // Mock successful registration
        authService.register.and.returnValue(of(void 0));

        // Mock successful login
        authManagerService.login.and.returnValue(of(loginResponse));

        // Set form values
        component.registrationForm.patchValue(registerUser);

        // Act
        component.onSubmit();

        // Assert
        expect(authService.register).toHaveBeenCalledWith(registerUser);
        expect(authManagerService.rememberMe).toBe(true);
        expect(authManagerService.login).toHaveBeenCalledWith(loginCredentials);
        expect(notificationService.success).toHaveBeenCalledWith('Registration successful! You are now logged in.');
    });

    it('should handle registration failure', () => {
        // Arrange
        const registerUser: RegisterUser = {
            email: 'test@example.com',
            password: 'password123',
            confirmPassword: 'password123',
            fullName: 'Test User'
        };

        const errorMessage = 'Registration failed';
        authService.register.and.returnValue(throwError(() => new Error(errorMessage)));

        // Set form values
        component.registrationForm.patchValue(registerUser);

        // Act
        component.onSubmit();

        // Assert
        expect(authService.register).toHaveBeenCalledWith(registerUser);
        expect(authManagerService.login).not.toHaveBeenCalled();
        expect(notificationService.error).toHaveBeenCalledWith(errorMessage);
    });

    it('should handle auto-login failure after successful registration', () => {
        // Arrange
        const registerUser: RegisterUser = {
            email: 'test@example.com',
            password: 'password123',
            confirmPassword: 'password123',
            fullName: 'Test User'
        };

        const loginCredentials: LoginUser = {
            email: 'test@example.com',
            password: 'password123',
            twoFactorCode: '',
            toFactorRecoveryCode: '',
            rememberMe: true
        };

        const errorMessage = 'Login failed';

        // Mock successful registration
        authService.register.and.returnValue(of(void 0));

        // Mock failed login
        authManagerService.login.and.returnValue(throwError(() => new Error(errorMessage)));

        // Set form values
        component.registrationForm.patchValue(registerUser);

        // Act
        component.onSubmit();

        // Assert
        expect(authService.register).toHaveBeenCalledWith(registerUser);
        expect(authManagerService.login).toHaveBeenCalledWith(loginCredentials);
        expect(notificationService.error).toHaveBeenCalledWith(`Registration successful, but login failed: ${errorMessage}`);
    });
}); 