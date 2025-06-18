/**
 * Represents a single answer submitted for a question.
 */
export class AnswerModel {
  questionId: number;
  submittedAnswer: string;

  constructor(data: any = {}) {
    this.questionId = data.questionId;
    this.submittedAnswer = data.submittedAnswer;
  }
}
