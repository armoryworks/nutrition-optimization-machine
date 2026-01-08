import { Component, OnInit, input, output, signal, effect } from '@angular/core';
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

interface OnboardingQuestion {
    id: string;
    text: string;
    type: 'text' | 'radio' | 'checkbox' | 'number';
    options?: string[];
    required?: boolean;
}

interface OnboardingAnswer {
    questionIndex: number;
    question: OnboardingQuestion;
    answer: string | number | boolean;
}

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
    questions = input<OnboardingQuestion[]>([]);
    isLoading = input(false);
    isSubmitting = input(false);
    error = input<string | null>(null);
    submitMessage = input<string | null>(null);

    answerSubmitted = output<OnboardingAnswer>();
    previousQuestion = output<void>();
    nextQuestion = output<void>();
    goToDashboard = output<void>();

    currentQuestionIndex = signal(0);
    currentQuestion: OnboardingQuestion | null = null;
    currentAnswerForm: FormGroup;

    constructor() {
        this.currentAnswerForm = new FormGroup({
            answer: new FormControl('', [Validators.required])
        });

        // Effect to update current question when questions input or currentQuestionIndex changes
        effect(() => {
            this.updateCurrentQuestion();
        });
    }

    ngOnInit(): void {
        this.updateCurrentQuestion();
    }

    private updateCurrentQuestion(): void {
        if (this.questions() && this.questions().length > 0 && this.currentQuestionIndex() < this.questions().length) {
            this.currentQuestion = this.questions()[this.currentQuestionIndex()];
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

    onAnswerChange(value: string | number | boolean): void {
        if (this.currentQuestion) {
            this.currentAnswerForm.patchValue({
                answer: value
            });
        }
    }

    goToPreviousQuestion(): void {
        if (this.currentQuestionIndex() > 0) {
            this.currentQuestionIndex.set(this.currentQuestionIndex() - 1);
            this.updateCurrentQuestion();
            this.previousQuestion.emit();
        }
    }

    goToNextQuestion(): void {
        if (this.currentAnswerForm.valid && this.currentQuestion) {
            const answer = this.currentAnswerForm.get('answer')?.value;
            this.answerSubmitted.emit({
                questionIndex: this.currentQuestionIndex(),
                question: this.currentQuestion,
                answer: answer
            });

            if (this.currentQuestionIndex() < this.questions().length - 1) {
                this.currentQuestionIndex.set(this.currentQuestionIndex() + 1);
                this.updateCurrentQuestion();
                this.nextQuestion.emit();
            }
        }
    }

    onGoToDashboard(): void {
        this.goToDashboard.emit();
    }
} 