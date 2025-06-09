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
import { ActivatedRoute } from "@angular/router";

// Angular Material Imports
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { MatProgressBarModule } from "@angular/material/progress-bar";
import { AuthService } from "../../auth.service";
import { ResetPassword } from "../../models/reset-password";

@Component({
  selector: "app-reset-password",
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
  ],
  templateUrl: "./reset-password.component.html",
  styleUrls: ["./reset-password.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class ResetPasswordComponent implements OnInit {
  resetPasswordForm!: FormGroup;
  isLoading = false;
  email: string = "";
  resetCode: string = "";

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private snackBar: MatSnackBar,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.email = params["email"] || "";
      this.resetCode = params["code"] || "";

      this.resetPasswordForm = this.nonNullableFb.group(
        {
          email: [
            { value: this.email, disabled: this.email !== "" },
            [Validators.required, Validators.email],
          ],
          resetCode: [
            { value: this.resetCode, disabled: this.resetCode !== "" },
            Validators.required,
          ],
          newPassword: ["", [Validators.required, Validators.minLength(8)]],
          confirmNewPassword: ["", Validators.required],
        },
        { validators: this.passwordMatchValidator }
      );
    });
  }

  // Custom validator for password matching
  passwordMatchValidator(control: AbstractControl) {
    const newPassword = this.resetPasswordForm.get("newPassword")?.value;
    const confirmNewPassword =
      this.resetPasswordForm.get("confirmNewPassword")?.value;
    return newPassword === confirmNewPassword ? null : { mismatch: true };
  }

  /**
   * Handles the password reset form submission.
   */
  onSubmit(): void {
    if (this.resetPasswordForm.invalid) {
      this.snackBar.open(
        "Please fill all required fields and correct errors.",
        "Close",
        { duration: 3000 }
      );
      return;
    }

    this.isLoading = true;
    const formData = this.resetPasswordForm.getRawValue();
    const resetData: ResetPassword = {
      email: formData.email,
      resetCode: formData.resetCode,
      newPassword: formData.newPassword,
    };

    this.authService.resetPassword(resetData).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.snackBar.open(response.message, "Dismiss", { duration: 7000 });
        } else {
          this.snackBar.open(response.message, "Close", { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error("Password reset error:", error);
        this.snackBar.open(
          "An unexpected error occurred. Please try again.",
          "Close",
          { duration: 5000 }
        );
      },
    });
  }
}
