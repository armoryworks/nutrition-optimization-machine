import { RestrictionTypeEnum } from '../../restriction/enums/restriction-type.enum';
import { OnboardingCompleteRequestModel } from './onboarding-complete-request.model';

export interface OnboardingWorkflowStep {
    id: string;
    title: string;
    component: string;
    isRequired: boolean;
    dataProperty?: string | null;
    restrictionType?: RestrictionTypeEnum;
    condition?: (data: OnboardingCompleteRequestModel) => boolean;
}