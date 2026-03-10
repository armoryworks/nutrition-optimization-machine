export interface MessageThread {
  id: number;
  subject: string;
  lastMessageContent: string;
  lastMessageDate: string;
  lastMessageSenderName: string;
  participantNames: string[];
  unreadCount: number;
  isPinned: boolean;
  isArchived: boolean;
  threadType: number;
  recipeId: number | null;
  ingredientId: number | null;
  planId: number | null;
}
