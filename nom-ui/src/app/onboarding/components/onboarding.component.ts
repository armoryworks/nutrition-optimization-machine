import { Component, OnInit } from '@angular/core';
import { QuestionService } from '../../services/question.service';
import { AuthService } from '../../services/auth.service';
import { QuestionAnswerSubmission } from '../../models/question-answer-submission.model';

@Component({
  selector: 'app-onboarding',
  templateUrl: './onboarding.component.html',
  styleUrls: ['./onboarding.component.scss'],
})
export class OnboardingComponent implements OnInit {
  answers: { questionId: number; submittedAnswer: string }[] = [];

  constructor(
    private questionService: QuestionService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    // Initialization logic here
  }

  submitAnswers() {
    // Get the person ID from your authentication service or user context
    const personId = this.authService.getPersonId(); // Replace with your actual method

    if (!personId) {
      console.error('Person ID not found.');
      return;
    }

    const submission: QuestionAnswerSubmission = {
      answers: this.answers.map((answer) => ({
        questionId: answer.questionId,
        submittedAnswer: answer.submittedAnswer,
      })),
    };

    this.questionService
      .submitOnboardingAnswers(personId, submission)
      .subscribe({
        next: (response) => {
          console.log('Answers submitted successfully', response);
          // Handle success (e.g., navigate to the next page)
        },
        error: (error) => {
          console.error('Error submitting answers', error);
          // Handle error
        },
      });
  }
}
