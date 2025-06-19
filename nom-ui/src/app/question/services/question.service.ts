import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { QuestionModel } from '../models/question.model';
import { SubmitAnswersRequestModel } from '../models/submit-answers-request.model';

/**
 * Service for managing questions and answers.
 * Provides methods to fetch onboarding questions and submit answers to the backend.
 */
@Injectable({
  providedIn: 'root',
})
export class QuestionService {
  /**
   * Constructor for QuestionService.
   * @param http - HttpClient for making HTTP requests.
   */
  constructor(private http: HttpClient) {}

  /**
   * Fetches onboarding questions from the backend.
   * Processes the 'defaultAnswer' field to create an 'options' array for select-type questions.
   * @returns An observable containing a list of questions.
   */
  getOnboardingQuestions(): Observable<QuestionModel[]> {
    return this.http
      .get<QuestionModel[]>('/api/Question/onboarding')
      .pipe(tap((questions) => console.log('Fetched questions:', questions)));
  }

  /**
   * Submits answers to the backend.
   * @param payload - The payload containing answers to submit.
   * @returns An observable for the submission result.
   */
  submitOnboardingAnswers(payload: SubmitAnswersRequestModel): Observable<any> {
    return this.http
      .post<any>('/api/answers', payload)
      .pipe(tap((response) => console.log('Submitted answers:', response)));
  }
}
