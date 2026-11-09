export const NOTICE_STATUSES = [
  { label: "Application Sent", tone: "sent" },
  { label: "Interviewing", tone: "interviewing" },
  { label: "Accepted", tone: "accepted" },
  { label: "Rejected", tone: "rejected" },
] as const;

export type NoticeStatus = (typeof NOTICE_STATUSES)[number]["label"];
export type NoticeTone = (typeof NOTICE_STATUSES)[number]["tone"];

export function parseStatus(value: unknown): NoticeStatus | null {
  if (typeof value !== "string") return null;
  const match = NOTICE_STATUSES.find((status) => status.label === value);
  return match?.label ?? null;
}

export function toneFor(status: NoticeStatus): NoticeTone {
  const match = NOTICE_STATUSES.find((item) => item.label === status);
  if (match === undefined) throw new Error("Unknown notice status.");
  return match.tone;
}
