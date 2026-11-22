export interface AttendanceSaveResult {
  created: number;
  updated: number;
  notificationsQueued: number;
}

export function attendanceSaveMessage(result: AttendanceSaveResult): string {
  return `${result.created} records created, ${result.updated} updated, ${result.notificationsQueued} parent notifications queued.`;
}
