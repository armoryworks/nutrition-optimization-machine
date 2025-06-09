import { Component, OnInit, ViewEncapsulation } from "@angular/core";
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from "@angular/forms";
import { MatSnackBar } from "@angular/material/snack-bar";
import { CommonModule } from "@angular/common";
import { RouterLink } from "@angular/router"; // Import RouterLink for button navigation

// Angular Material Imports
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatToolbarModule } from "@angular/material/toolbar";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { MatProgressBarModule } from "@angular/material/progress-bar";
import { AuthService } from "../../auth.service";
import { RegisterUser } from "../../models/register-user";

@Component({
  selector: "app-registration",
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatToolbarModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    RouterLink, // Add RouterLink
  ],
  templateUrl: "./registration.component.html",
  styleUrls: ["./registration.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class RegistrationComponent implements OnInit {
  registrationForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.registrationForm = this.nonNullableFb.group(
      {
        email: ["", [Validators.required, Validators.email]],
        password: ["", [Validators.required, Validators.minLength(8)]],
        confirmPassword: ["", Validators.required],
        acceptTerms: [false, Validators.requiredTrue],
      },
      { validators: this.passwordMatchValidator }
    );
  }

  // Custom validator for password matching
  passwordMatchValidator(control: AbstractControl) {
    const password = control.get("password")?.value;
    const confirmPassword = control.get("confirmPassword")?.value;
    return password === confirmPassword ? null : { mismatch: true };
  }

  /**
   * Handles the registration form submission.
   */
  onSubmit(): void {
    if (this.registrationForm.invalid) {
      this.snackBar.open(
        "Please fill all required fields and correct errors.",
        "Close",
        { duration: 3000 }
      );
      return;
    }

    this.isLoading = true;
    const userData: RegisterUser = this.registrationForm.getRawValue();

    this.authService.register(userData).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.snackBar.open(response.message, "Dismiss", { duration: 5000 });
        } else {
          this.snackBar.open(response.message, "Close", { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error("Registration error:", error);
        this.snackBar.open(
          "An unexpected error occurred. Please try again.",
          "Close",
          { duration: 5000 }
        );
      },
    });
  }
}
