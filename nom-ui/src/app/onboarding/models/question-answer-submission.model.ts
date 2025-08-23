export interface QuestionAnswerSubmission {
    questionIndex: number;
    question: OnboardingQuestion;
    answer: string | number | boolean;
}

export interface OnboardingQuestion {
    id: number;
    text: string;
    type: 'text' | 'number' | 'boolean' | 'select';
    options?: string[];
    required: boolean;
    order: number;
}

