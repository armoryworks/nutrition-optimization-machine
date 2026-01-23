import { UserClaim } from './user-claim.interface';

export interface UserInfo {
    personId: number;
    householdId?: number;
    userId?: string;
    email?: string;
    userName?: string;
    claims: UserClaim[];
}






