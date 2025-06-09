import { Component, OnInit, ViewEncapsulation } from "@angular/core";
import { ActivatedRoute, RouterLink } from "@angular/router"; // Import RouterLink for button navigation
import { MatSnackBar } from "@angular/material/snack-bar";

import { CommonModule } from "@angular/common";

// Angular Material Imports
import { MatCardModule } from "@angular/material/card";
import { MatButtonModule } from "@angular/material/button";
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { MatProgressBarModule } from "@angular/material/progress-bar";
import { MatIconModule } from "@angular/material/icon";
import { AuthService } from "../../auth.service";
import { ConfirmEmail } from "../../models/confirm-email";

@Component({
  selector: "app-confirm-email",
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatIconModule,
    RouterLink, // Add RouterLink for navigation
  ],
  templateUrl: "./confirm-email.component.html",
  styleUrls: ["./confirm-email.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class ConfirmEmailComponent implements OnInit {
  isLoading = true;
  confirmationStatus: "loading" | "success" | "failure" = "loading";
  message: string = "Confirming your email address...";
  userId: string = "";
  code: string = "";
  changedEmail: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private authService: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.userId = params["userId"] || "";
      this.code = params["code"] || "";
      this.changedEmail = params["changedEmail"] || null;

      if (this.userId && this.code) {
        this.sendConfirmation();
      } else {
        this.confirmationStatus = "failure";
        this.message = "Invalid confirmation link. Missing user ID or code.";
        this.isLoading = false;
        this.snackBar.open(this.message, "Close", { duration: 5000 });
      }
    });
  }

  private sendConfirmation(): void {
    this.isLoading = true;
    const confirmData: ConfirmEmail = {
      userId: this.userId,
      code: this.code,
      changedEmail: this.changedEmail || undefined,
    };

    this.authService.confirmEmail(confirmData).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.confirmationStatus = "success";
          this.message = response.message;
          this.snackBar.open(response.message, "Dismiss", { duration: 7000 });
        } else {
          this.confirmationStatus = "failure";
          this.message = response.message;
          this.snackBar.open(response.message, "Close", { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        this.confirmationStatus = "failure";
        this.message =
          "An unexpected error occurred during email confirmation. Please try again or contact support.";
        console.error("Email confirmation error:", error);
        this.snackBar.open(this.message, "Close", { duration: 5000 });
      },
    });
  }
}
