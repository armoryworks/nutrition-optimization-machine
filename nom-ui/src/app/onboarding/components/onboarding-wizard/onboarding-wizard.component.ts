import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatRadioModule } from '@angular/material/radio';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatCardModule } from '@angular/material/card';

@Component({
    selector: 'nom-onboarding-wizard',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatProgressSpinnerModule,
        MatProgressBarModule,
        MatRadioModule,
        MatCheckboxModule,
        MatCardModule,
    ],
    templateUrl: './onboarding-wizard.component.html',
    styleUrls: ['./onboarding-wizard.component.scss'],
})
export class OnboardingWizardComponent implements OnInit {
    @Input() questions: any[] = [];
    @Input() isLoading: boolean = false;
    @Input() isSubmitting: boolean = false;
    @Input() error: string | null = null;
    @Input() submitMessage: string | null = null;

    @Output() answerSubmitted = new EventEmitter<any>();
    @Output() previousQuestion = new EventEmitter<void>();
    @Output() nextQuestion = new EventEmitter<void>();
    @Output() goToDashboard = new EventEmitter<void>();

    currentQuestionIndex: number = 0;
    currentQuestion: any = null;
    currentAnswerForm: FormGroup;

    constructor() {
        this.currentAnswerForm = new FormGroup({
            answer: new FormControl('', [Validators.required])
        });
    }

    ngOnInit(): void {
        this.updateCurrentQuestion();
    }

    ngOnChanges(): void {
        this.updateCurrentQuestion();
    }

    private updateCurrentQuestion(): void {
        if (this.questions && this.questions.length > 0 && this.currentQuestionIndex < this.questions.length) {
            this.currentQuestion = this.questions[this.currentQuestionIndex];
            this.resetForm();
        }
    }

    private resetForm(): void {
        if (this.currentQuestion) {
            this.currentAnswerForm.patchValue({
                answer: ''
            });
        }
    }

    onAnswerChange(value: any): void {
        this.currentAnswerForm.patchValue({
            answer: value
        });
    }

    goToPreviousQuestion(): void {
        if (this.currentQuestionIndex > 0) {
            this.currentQuestionIndex--;
            this.updateCurrentQuestion();
            this.previousQuestion.emit();
        }
    }

    goToNextQuestion(): void {
        if (this.currentAnswerForm.valid) {
            const answer = this.currentAnswerForm.get('answer')?.value;
            this.answerSubmitted.emit({
                questionIndex: this.currentQuestionIndex,
                question: this.currentQuestion,
                answer: answer
            });

            if (this.currentQuestionIndex < this.questions.length - 1) {
                this.currentQuestionIndex++;
                this.updateCurrentQuestion();
                this.nextQuestion.emit();
            }
        }
    }

    onGoToDashboard(): void {
        this.goToDashboard.emit();
    }
} 