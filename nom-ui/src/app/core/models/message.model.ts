export interface Message {
  id: number;
  threadId: number;
  senderPersonId: number;
  senderName: string;
  content: string;
  timestamp: string;
  isRead: boolean;
}
