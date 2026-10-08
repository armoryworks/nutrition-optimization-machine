export interface Message {
  id: number;
  messageThreadId: number;
  senderPersonId: number;
  senderDisplayName: string;
  content: string;
  timestamp: string;
  isRead: boolean;
}
