export const NOTICE_STATUSES = [
  { label: "Application Sent", tone: "sent" },
  { label: "Interviewing", tone: "interviewing" },
  { label: "Rejected", tone: "rejected" },
] as const;

export type NoticeStatus = (typeof NOTICE_STATUSES)[number]["label"];
export type NoticeTone = (typeof NOTICE_STATUSES)[number]["tone"];

export type StatusGroup<T> = {
  readonly label: NoticeStatus;
  readonly tone: NoticeTone;
  readonly notices: readonly T[];
};

export function parseStatus(value: unknown): NoticeStatus | null {
  if (typeof value !== "string") return null;
  const match = NOTICE_STATUSES.find((status) => status.label === value);
  return match?.label ?? null;
}

export function groupByStatus<T extends { readonly status: string }>(
  notices: readonly T[],
): readonly StatusGroup<T>[] | null {
  const buckets = new Map<NoticeStatus, T[]>(
    NOTICE_STATUSES.map((status) => [status.label, []]),
  );

  for (const notice of notices) {
    const status = parseStatus(notice.status);
    if (status === null) return null;
    const bucket = buckets.get(status);
    if (bucket === undefined) return null;
    bucket.push(notice);
  }

  return NOTICE_STATUSES.map((status) => ({
    label: status.label,
    tone: status.tone,
    notices: buckets.get(status.label) ?? [],
  }));
}
