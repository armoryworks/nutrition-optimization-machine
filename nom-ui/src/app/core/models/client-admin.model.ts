/** Admin-portal summary of one household ("client") on this instance. */
export interface AdminHousehold {
  id: number;
  name: string;
  isPersonal: boolean;
  /** External management marker (e.g. "brigade:456"); null = self-managed. */
  managedBy: string | null;
  memberCount: number;
  activeMemberCount: number;
  createdDate: string;
  /** Date of the most recent meal-plan slot; null = never planned. */
  lastPlanDate: string | null;
}

/** Admin-portal view of one member within a household. */
export interface AdminHouseholdMember {
  personId: number;
  name: string;
  /** Identity user id when the person has a login; null for profile-only members. */
  userId: string | null;
  email: string | null;
  role: string;
  isActive: boolean;
  isAdmin: boolean;
  joinedDate: string | null;
}
