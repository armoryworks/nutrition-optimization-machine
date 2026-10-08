import { Message } from './message.model';

export interface MessageParticipant {
  id: number;
  displayName: string;
  email: string;
  avatarUrl: string | null;
  isOnline: boolean;
  lastSeen: string | null;
}

export interface MessageThread {
  id: number;
  participants: MessageParticipant[];
  lastMessage: Message | null;
  lastActivity: string | null;
  unreadCount: number;
  isPinned: boolean;
  isArchived: boolean;
  threadType: number;
  recipeId: number | null;
  ingredientId: number | null;
  planId: number | null;
}
