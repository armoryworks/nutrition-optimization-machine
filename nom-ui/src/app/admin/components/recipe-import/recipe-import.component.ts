// src/app/admin/components/recipe-import/recipe-import.component.ts
import {
  Component,
  OnDestroy,
  OnInit,
  HostListener,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

// Import your existing service and models/enums
import { AdminService } from '../../services/admin.service';
import { RecipeImportResponseModel } from '../../models/recipe-import-response.model';
import { ImportJobStatusResponseModel } from '../../models/import-job-status-response.model';
import { ImportStatusEnum } from '../../enums/import-status.enum';

import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, interval, of } from 'rxjs';
import { startWith, switchMap, catchError } from 'rxjs/operators';

@Component({
  selector: 'app-recipe-import',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatSnackBarModule,
  ],
  templateUrl: './recipe-import.component.html',
  styleUrls: ['./recipe-import.component.scss'], // Note: Using .scss as per your file
})
export class RecipeImportComponent implements OnInit, OnDestroy {
  // UI related signals for file selection and drag/drop
  isDragging = signal(false);
  selectedFile = signal<File | null>(null);
  selectedFileName = signal<string>('');

  // Import job status and polling
  processId: string | null = null;
  jobStatus: ImportJobStatusResponseModel | null = null;
  isImporting = false; // Indicates if an import process has been initiated and is running/polling
  isPolling = false; // Indicates if the polling interval is currently active
  pollingSubscription: Subscription | null = null;

  // Make the enum available in the template
  ImportStatusEnum = ImportStatusEnum;

  constructor(
    private adminService: AdminService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    // You might check local storage or route params for a processId to resume polling here.
    // For simplicity, this example does not include resume logic.
  }

  /**
   * Prevents default browser behavior for drag events (e.g., opening file)
   * and sets isDragging to true for visual feedback.
   * @param event The DragEvent
   */
  @HostListener('dragover', ['$event']) onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging.set(true);
  }

  /**
   * Resets isDragging to false when the dragged item leaves the drop zone.
   * @param event The DragEvent
   */
  @HostListener('dragleave', ['$event']) onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging.set(false);
  }

  /**
   * Handles the file drop event. Extracts files and sets the selected file.
   * @param event The DragEvent
   */
  @HostListener('drop', ['$event']) onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging.set(false); // Reset dragging state

    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      this.handleFiles(event.dataTransfer.files);
    }
  }

  /**
   * Handles file selection from the traditional file input.
   * @param event The ChangeEvent from the file input
   */
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.handleFiles(input.files);
      // Reset input value to allow selecting the same file again if needed
      input.value = '';
    }
  }

  /**
   * Processes the FileList (from either drag-drop or input).
   * For this component, it only allows a single CSV file.
   * @param files The FileList object
   */
  private handleFiles(files: FileList): void {
    const file = files[0];
    if (file && file.name.toLowerCase().endsWith('.csv')) {
      this.selectedFile.set(file);
      this.selectedFileName.set(file.name);
      this.snackBar.open(`File selected: ${file.name}`, 'Dismiss', {
        duration: 3000,
      });
    } else {
      this.selectedFile.set(null);
      this.selectedFileName.set('');
      this.snackBar.open(
        'Please select a valid CSV file (e.g., .csv extension).',
        'Dismiss',
        { duration: 5000, panelClass: ['error-snackbar'] }
      );
    }
  }

  /**
   * Clears the currently selected file from the UI.
   */
  clearSelection(): void {
    this.selectedFile.set(null);
    this.selectedFileName.set('');
    this.snackBar.open('File selection cleared.', 'Dismiss', {
      duration: 2000,
    });
  }

  /**
   * Initiates the recipe import process by uploading the selected file.
   * This method assumes AdminService has a method 'initiateRecipeImportWithFile'
   * that can handle FormData or a File object directly.
   * You WILL NEED to implement this method in your AdminService.
   * For example:
   * initiateRecipeImportWithFile(file: File, jobName: string): Observable<RecipeImportResponseModel> {
   * const formData = new FormData();
   * formData.append('file', file, file.name);
   * formData.append('jobName', jobName); // Or use other relevant metadata
   * return this.http.post<RecipeImportResponseModel>('/api/admin/recipe-import-file', formData);
   * }
   */
  initiateImport(): void {
    const file = this.selectedFile();
    if (!file) {
      this.snackBar.open('No file selected for import.', 'Dismiss', {
        duration: 3000,
      });
      return;
    }

    this.isImporting = true;
    this.jobStatus = null; // Clear previous status
    this.stopPolling(); // Ensure any existing polling is stopped

    // Use the selected file's name as the job name for the import request
    const jobName = file.name;

    // IMPORTANT: This call assumes your AdminService has a method to upload the file
    // and initiate the import, e.g., 'initiateRecipeImportWithFile'.
    // You MUST implement this method in your AdminService to send the File content.
    this.adminService.initiateRecipeImportWithFile(file, jobName).subscribe({
      next: (response: RecipeImportResponseModel) => {
        if (response.success && response.processId) {
          this.processId = response.processId;
          this.snackBar.open(
            'Import initiated! Polling for status...',
            'Dismiss',
            { duration: 5000, panelClass: ['success-snackbar'] }
          );
          this.startPolling(); // Start polling for status updates
        } else {
          this.snackBar.open(
            `Failed to start import: ${
              response.message || 'Unknown error during initiation.'
            }`,
            'Dismiss',
            { panelClass: ['error-snackbar'] }
          );
          this.isImporting = false;
        }
      },
      error: (error: HttpErrorResponse) => {
        this.snackBar.open(
          `Error initiating import: ${error.message || 'Unknown error'}`,
          'Dismiss',
          { panelClass: ['error-snackbar'] }
        );
        this.isImporting = false;
        this.handleHttpError(error);
      },
    });
  }

  /**
   * Refreshes the import job status immediately.
   */
  refreshStatus(): void {
    if (!this.processId) {
      this.snackBar.open(
        'No process ID available to refresh status.',
        'Dismiss',
        { duration: 3000 }
      );
      return;
    }
    this.startPolling(); // Calling startPolling will trigger an immediate fetch and then set up interval
  }

  /**
   * Starts polling the import job status from the backend.
   */
  startPolling(): void {
    if (!this.processId) {
      this.snackBar.open('No process ID available to poll.', 'Dismiss', {
        duration: 3000,
      });
      this.isPolling = false;
      this.isImporting = false; // Ensure import state is off if no processId
      return;
    }

    this.isPolling = true;
    this.isImporting = true; // Show loading while polling is active
    this.stopPolling(); // Ensure only one polling subscription is active

    // Poll every 5 seconds
    this.pollingSubscription = interval(5000)
      .pipe(
        startWith(0), // Emit immediately on subscription to fetch status right away
        switchMap(() => this.adminService.getImportStatus(this.processId!)),
        catchError((error: HttpErrorResponse) => {
          this.snackBar.open(
            `Error polling status: ${error.message || 'Unknown error'}`,
            'Dismiss',
            { panelClass: ['error-snackbar'] }
          );
          this.handleHttpError(error);
          this.stopPolling(); // Stop polling on error
          this.isImporting = false; // Turn off importing state on error
          return of(null); // Return observable of null to gracefully complete stream
        })
      )
      .subscribe({
        next: (statusResponse: ImportJobStatusResponseModel | null) => {
          this.jobStatus = statusResponse;
          this.isImporting = true; // Keep true as long as polling is active for ongoing import

          if (this.jobStatus) {
            if (
              this.jobStatus.status === ImportStatusEnum.Completed ||
              this.jobStatus.status === ImportStatusEnum.Failed ||
              this.jobStatus.status === ImportStatusEnum.Canceled
            ) {
              const statusMessage = `Import ${
                this.jobStatus.jobName
              } ${ImportStatusEnum[this.jobStatus.status].toLowerCase()}!`;
              const panelClass =
                this.jobStatus.status === ImportStatusEnum.Completed
                  ? ['success-snackbar']
                  : ['error-snackbar'];
              this.snackBar.open(statusMessage, 'Dismiss', {
                duration: 5000,
                panelClass,
              });
              this.stopPolling();
              this.isImporting = false; // Import process has finished
              this.clearSelection(); // Clear selected file after job completion/failure
              this.processId = null; // Clear process ID
            }
          }
        },
        error: (error: HttpErrorResponse) => {
          // This block might catch errors missed by catchError in the pipe or other RxJS stream issues.
          this.snackBar.open(
            `Unexpected polling error: ${error.message}`,
            'Dismiss',
            { panelClass: ['error-snackbar'] }
          );
          this.isImporting = false;
          this.stopPolling();
        },
      });
  }

  /**
   * Stops the ongoing polling for job status.
   */
  stopPolling(): void {
    if (this.pollingSubscription) {
      this.pollingSubscription.unsubscribe();
      this.pollingSubscription = null;
    }
    this.isPolling = false;
    // Do NOT set isImporting to false here unless the job definitively finished
    // isImporting should remain true if job is still Queued/Running but polling was manually stopped.
  }

  /**
   * Getter to display human-readable status text.
   */
  get currentStatusText(): string {
    if (!this.jobStatus) {
      return 'N/A';
    }
    return ImportStatusEnum[this.jobStatus.status];
  }

  // Getters for template to improve readability and handle potential nulls
  get isQueuedOrRunning(): boolean {
    return (
      this.jobStatus?.status === ImportStatusEnum.Queued ||
      this.jobStatus?.status === ImportStatusEnum.Running
    );
  }

  get isCompleted(): boolean {
    return this.jobStatus?.status === ImportStatusEnum.Completed;
  }

  get isFailedOrCanceled(): boolean {
    return (
      this.jobStatus?.status === ImportStatusEnum.Failed ||
      this.jobStatus?.status === ImportStatusEnum.Canceled
    );
  }

  get progressPercentage(): number {
    if (!this.jobStatus || this.jobStatus.totalRecords === 0) {
      return 0;
    }
    const processed =
      this.jobStatus.importedCount +
      this.jobStatus.skippedCount +
      this.jobStatus.errorCount;
    return (processed / this.jobStatus.totalRecords) * 100;
  }

  /**
   * Handles HTTP errors and logs them.
   * @param error The HttpErrorResponse object.
   */
  private handleHttpError(error: HttpErrorResponse): void {
    if (error.error instanceof ErrorEvent) {
      console.error('Client-side error occurred:', error.error.message);
    } else {
      console.error(
        `Backend returned code ${error.status}, ` +
          `body was: ${JSON.stringify(error.error)}`
      );
    }
    // MatSnackBar already displays a message.
  }

  ngOnDestroy(): void {
    this.stopPolling(); // Clean up subscription on component destruction
  }
}
