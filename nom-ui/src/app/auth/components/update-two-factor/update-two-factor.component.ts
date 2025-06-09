import { Component, OnInit, ViewEncapsulation } from "@angular/core";
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from "@angular/forms";
import { MatSnackBar } from "@angular/material/snack-bar";
import { CommonModule } from "@angular/common";

// Angular Material Imports
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatSlideToggleModule } from "@angular/material/slide-toggle";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { MatProgressBarModule } from "@angular/material/progress-bar";
import { MatTooltipModule } from "@angular/material/tooltip"; // For tooltips
import { AuthService } from "../../auth.service";
import { UpdateTwoFactor } from "../../models/update-two-factor";
import { MatDividerModule } from "@angular/material/divider";

@Component({
  selector: "app-update-two-factor",
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatCheckboxModule,
    MatSlideToggleModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatTooltipModule, // Add MatTooltipModule
  ],
  templateUrl: "./update-two-factor.component.html",
  styleUrls: ["./update-two-factor.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class UpdateTwoFactorComponent implements OnInit {
  twoFactorForm!: FormGroup;
  isLoading = false;
  is2faEnabled: boolean = false; // Mock user's current 2FA status
  sharedKey: string = "MOCK_SHARED_KEY_ABC123"; // Mock shared key for setup
  recoveryCodes: string[] = ["CODE1", "CODE2", "CODE3", "CODE4", "CODE5"]; // Mock recovery codes

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.twoFactorForm = this.nonNullableFb.group({
      enable: [this.is2faEnabled],
      twoFactorCode: ["", [Validators.pattern(/^\d{6}$/)]], // Code is required conditionally
      resetSharedKey: [false],
      resetRecoverCodes: [false],
      forgetMachine: [false],
    });

    // Add conditional validation for twoFactorCode
    this.twoFactorForm.get("enable")?.valueChanges.subscribe((enabled) => {
      this.is2faEnabled = enabled;
      const twoFactorCodeControl = this.twoFactorForm.get("twoFactorCode");
      if (twoFactorCodeControl) {
        if (enabled) {
          twoFactorCodeControl.setValidators([
            Validators.required,
            Validators.pattern(/^\d{6}$/),
          ]);
          twoFactorCodeControl.enable();
        } else {
          twoFactorCodeControl.clearValidators();
          twoFactorCodeControl.disable();
          twoFactorCodeControl.setValue(""); // Clear value when disabling
        }
        twoFactorCodeControl.updateValueAndValidity();
      }
    });

    // Initially disable code if 2FA is not enabled (based on initial this.is2faEnabled)
    if (!this.is2faEnabled) {
      this.twoFactorForm.get("twoFactorCode")?.disable();
    }
  }

  /**
   * Handles the 2FA form submission.
   */
  onSubmit(): void {
    // If disabling 2FA, clear twoFactorCode validators if not enabling
    if (!this.twoFactorForm.get("enable")?.value) {
      this.twoFactorForm.get("twoFactorCode")?.clearValidators();
      this.twoFactorForm.get("twoFactorCode")?.updateValueAndValidity();
    }

    this.twoFactorForm.markAllAsTouched();
    if (this.twoFactorForm.invalid) {
      this.snackBar.open("Please correct the form errors.", "Close", {
        duration: 3000,
      });
      return;
    }

    this.isLoading = true;
    const data: UpdateTwoFactor = this.twoFactorForm.getRawValue();

    this.authService.updateTwoFactorAuth(data).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.snackBar.open(response.message, "Dismiss", { duration: 7000 });
          this.is2faEnabled = data.enable; // Update local state
          // Reset only relevant parts of the form based on success
          if (data.enable) {
            this.twoFactorForm.get("twoFactorCode")?.setValue("");
            this.twoFactorForm.get("twoFactorCode")?.markAsUntouched();
            this.twoFactorForm.get("resetSharedKey")?.setValue(false);
            this.twoFactorForm.get("resetRecoverCodes")?.setValue(false);
          } else {
            this.twoFactorForm.reset({
              enable: false,
              twoFactorCode: "",
              resetSharedKey: false,
              resetRecoverCodes: false,
              forgetMachine: false,
            });
          }
        } else {
          this.snackBar.open(response.message, "Close", { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error("Update 2FA error:", error);
        this.snackBar.open(
          "An unexpected error occurred. Please try again.",
          "Close",
          { duration: 5000 }
        );
      },
    });
  }

  // Helper to copy recovery codes to clipboard
  copyRecoveryCodes(): void {
    if (this.recoveryCodes && this.recoveryCodes.length > 0) {
      const codesText = this.recoveryCodes.join("\n");
      const textarea = document.createElement("textarea");
      textarea.value = codesText;
      textarea.style.position = "fixed";
      document.body.appendChild(textarea);
      textarea.select();
      try {
        document.execCommand("copy");
        this.snackBar.open("Recovery codes copied to clipboard!", "Dismiss", {
          duration: 3000,
        });
      } catch (err) {
        console.error("Could not copy text: ", err);
        this.snackBar.open("Failed to copy recovery codes.", "Close", {
          duration: 3000,
        });
      } finally {
        document.body.removeChild(textarea);
      }
    }
  }
}
