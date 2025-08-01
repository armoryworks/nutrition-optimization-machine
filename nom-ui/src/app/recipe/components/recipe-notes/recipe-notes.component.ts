import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ReactiveFormsModule, NonNullableFormBuilder, Validators } from "@angular/forms";
import { MatCardModule } from "@angular/material/card";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatDividerModule } from "@angular/material/divider";
import { MatChipsModule } from "@angular/material/chips";
import { MatSnackBar } from "@angular/material/snack-bar";
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { Subject, takeUntil } from "rxjs";

import { RecipeAdvancedService } from "../../services/recipe-advanced.service";
import { RecipeNoteModel, RecipeNoteCreateModel } from "../../models/recipe-note.model";

@Component({
    selector: "app-recipe-notes",
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatDividerModule,
        MatChipsModule,
        MatTooltipModule,
        MatCheckboxModule,
    ],
    templateUrl: "./recipe-notes.component.html",
    styleUrls: ["./recipe-notes.component.scss"],
})
export class RecipeNotesComponent implements OnInit, OnDestroy {
    @Input() recipeId: number = 0;

    notes: RecipeNoteModel[] = [];
    noteForm = this.fb.group({
        noteTitle: ["", [Validators.required, Validators.maxLength(511)]],
        noteText: ["", [Validators.maxLength(2047)]],
        isPublic: [false],
    });

    isLoading = false;
    isSubmitting = false;
    editingNoteId: number | null = null;
    private destroy$ = new Subject<void>();

    constructor(
        private fb: NonNullableFormBuilder,
        private recipeAdvancedService: RecipeAdvancedService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadNotes();
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    loadNotes(): void {
        this.isLoading = true;
        this.recipeAdvancedService
            .getRecipeNotes(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (notes) => {
                    this.notes = notes.sort((a, b) =>
                        new Date(b.createdDate).getTime() - new Date(a.createdDate).getTime()
                    );
                    this.isLoading = false;
                },
                error: (error) => {
                    console.error("Error loading notes:", error);
                    this.snackBar.open("Failed to load notes", "Close", { duration: 3000 });
                    this.isLoading = false;
                },
            });
    }

    createNote(): void {
        if (this.noteForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        const request: RecipeNoteCreateModel = {
            recipeId: this.recipeId,
            noteTitle: this.noteForm.get("noteTitle")!.value,
            noteText: this.noteForm.get("noteText")!.value || undefined,
            isPublic: this.noteForm.get("isPublic")!.value,
        };

        this.recipeAdvancedService
            .createNote(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (note) => {
                    this.notes.unshift(note);
                    this.noteForm.reset({ isPublic: false });
                    this.snackBar.open("Note created successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating note:", error);
                    this.snackBar.open("Failed to create note", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
            });
    }

    startEditNote(note: RecipeNoteModel): void {
        this.editingNoteId = note.id;
        this.noteForm.patchValue({
            noteTitle: note.noteTitle,
            noteText: note.noteText || "",
            isPublic: note.isPublic,
        });
    }

    cancelEdit(): void {
        this.editingNoteId = null;
        this.noteForm.reset({ isPublic: false });
    }

    updateNote(): void {
        if (this.noteForm.invalid || this.isSubmitting || !this.editingNoteId) {
            return;
        }

        this.isSubmitting = true;
        const request: RecipeNoteCreateModel = {
            recipeId: this.recipeId,
            noteTitle: this.noteForm.get("noteTitle")!.value,
            noteText: this.noteForm.get("noteText")!.value || undefined,
            isPublic: this.noteForm.get("isPublic")!.value,
        };

        this.recipeAdvancedService
            .updateNote(this.editingNoteId, request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    const noteIndex = this.notes.findIndex(n => n.id === this.editingNoteId);
                    if (noteIndex !== -1) {
                        this.notes[noteIndex] = {
                            ...this.notes[noteIndex],
                            noteTitle: request.noteTitle,
                            noteText: request.noteText,
                            isPublic: request.isPublic,
                        };
                    }
                    this.editingNoteId = null;
                    this.noteForm.reset({ isPublic: false });
                    this.snackBar.open("Note updated successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error updating note:", error);
                    this.snackBar.open("Failed to update note", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
            });
    }

    deleteNote(noteId: number): void {
        if (confirm("Are you sure you want to delete this note?")) {
            this.recipeAdvancedService
                .deleteNote(noteId)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: () => {
                        this.notes = this.notes.filter((note) => note.id !== noteId);
                        this.snackBar.open("Note deleted successfully", "Close", { duration: 3000 });
                    },
                    error: (error) => {
                        console.error("Error deleting note:", error);
                        this.snackBar.open("Failed to delete note", "Close", { duration: 3000 });
                    },
                });
        }
    }

    isEditing(noteId: number): boolean {
        return this.editingNoteId === noteId;
    }

    formatDate(dateString: string): string {
        return new Date(dateString).toLocaleDateString("en-US", {
            year: "numeric",
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit",
        });
    }
} 