import { groupByStatus, parseStatus, type NoticeStatus, type StatusGroup } from "./statuses";

export const NOTICES_PATH = "/notices";

const NOTICES_API = "/api/notices";

const GUID =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const SENT_AT =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?Z$/;

export type Notice = {
  readonly id: string;
  readonly company: string;
  readonly role: string | null;
  readonly status: NoticeStatus;
  readonly sentAt: Date;
};

export type NoticesResult =
  | { kind: "signed-out" }
  | { kind: "failed" }
  | { kind: "empty"; email: string }
  | { kind: "ready"; email: string; groups: readonly StatusGroup<Notice>[] };

type NoticeDraft = {
  readonly id: string;
  readonly company: string;
  readonly role: string | null;
  readonly status: string;
  readonly sentAt: Date;
};

export async function loadNotices(signal?: AbortSignal): Promise<NoticesResult> {
  let response: Response;
  try {
    response = await fetch(NOTICES_API, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "failed" };
  }

  if (response.status === 401) return { kind: "signed-out" };
  if (response.status !== 200) return { kind: "failed" };

  let body: unknown;
  try {
    body = await response.json();
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "failed" };
  }

  const parsed = parsePayload(body);
  if (parsed === null) return { kind: "failed" };
  if (parsed.notices.length === 0) return { kind: "empty", email: parsed.email };

  const groups = groupByStatus(parsed.notices);
  if (groups === null) return { kind: "failed" };

  return {
    kind: "ready",
    email: parsed.email,
    groups: groups.map((group) => ({
      label: group.label,
      tone: group.tone,
      notices: group.notices.map((notice) => ({
        id: notice.id,
        company: notice.company,
        role: notice.role,
        status: group.label,
        sentAt: notice.sentAt,
      })),
    })),
  };
}

function parsePayload(value: unknown): { email: string; notices: NoticeDraft[] } | null {
  if (!isRecord(value)) return null;
  const email = parseEmail(value.email);
  if (email === null) return null;
  if (!Array.isArray(value.notices)) return null;

  const notices: NoticeDraft[] = [];
  for (const item of value.notices) {
    const notice = parseNotice(item);
    if (notice === null) return null;
    notices.push(notice);
  }
  return { email, notices };
}

function parseNotice(value: unknown): NoticeDraft | null {
  if (!isRecord(value)) return null;
  if (typeof value.id !== "string" || !GUID.test(value.id)) return null;

  const company = parseCompany(value.company);
  if (company === null) return null;

  const role = parseRole(value.role);
  if (role === undefined) return null;

  if (typeof value.status !== "string" || parseStatus(value.status) === null) return null;

  const sentAt = parseSentAt(value.sentAt);
  if (sentAt === null) return null;

  return { id: value.id, company, role, status: value.status, sentAt };
}

function parseEmail(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const email = value.trim();
  if (email.length === 0) return null;
  return email;
}

function parseCompany(value: unknown): string | null {
  if (typeof value !== "string") return null;
  if (value.trim().length === 0) return null;
  return value;
}

function parseRole(value: unknown): string | null | undefined {
  if (value === null) return null;
  if (typeof value !== "string" || value.length === 0) return undefined;
  return value;
}

function parseSentAt(value: unknown): Date | null {
  if (typeof value !== "string") return null;
  const match = SENT_AT.exec(value);
  if (match === null) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  if (month < 1 || month > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) {
    return null;
  }

  const sentAt = new Date(Date.UTC(year, month - 1, day, hour, minute, second));
  if (
    sentAt.getUTCFullYear() !== year ||
    sentAt.getUTCMonth() !== month - 1 ||
    sentAt.getUTCDate() !== day
  ) {
    return null;
  }

  const fraction = match[7];
  if (fraction !== undefined) {
    const milliseconds = Number(fraction.slice(0, 3).padEnd(3, "0"));
    sentAt.setUTCMilliseconds(milliseconds);
  }
  return sentAt;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isAbortError(error: unknown): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "name" in error &&
    error.name === "AbortError"
  );
}
