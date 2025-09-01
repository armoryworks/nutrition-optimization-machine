import { UserClaim } from './user-claim.interface';

export interface UserInfo {
    personId: number;
    userId?: string;
    email?: string;
    userName?: string;
    claims: UserClaim[];
}





