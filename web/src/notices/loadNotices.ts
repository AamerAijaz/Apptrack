import { forgetProfile } from "../auth/loadSessionProfile";
import { NOTICE_STATUSES, parseStatus, toneFor, type NoticeStatus, type NoticeTone } from "./statuses";

export const NOTICES_PATH = "/notices";

const BOARD_API = "/api/notices";
const LANE_API = "/api/notices/lane";

const GUID =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const SENT_AT =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?Z$/;

export type NoticeCard = {
  readonly id: string;
  readonly company: string;
  readonly role: string | null;
  readonly sentAt: Date;
};

export type NoticeLane = {
  readonly status: NoticeStatus;
  readonly tone: NoticeTone;
  readonly notices: readonly NoticeCard[];
  readonly limit: number;
};

export type BoardResult =
  | { kind: "signed-out" }
  | { kind: "failed" }
  | { kind: "ready"; email: string; lanes: readonly NoticeLane[] };

export type LanePageResult =
  | { kind: "signed-out" }
  | { kind: "failed" }
  | { kind: "ready"; email: string; status: NoticeStatus; notices: readonly NoticeCard[]; limit: number };

export async function loadBoard(signal?: AbortSignal): Promise<BoardResult> {
  const response = await request(BOARD_API, signal);
  if (response.kind !== "ok") return response;

  const parsed = parseBoard(response.body);
  if (parsed === null) return { kind: "failed" };
  return { kind: "ready", email: parsed.email, lanes: parsed.lanes };
}

export async function getNotices(
  status: NoticeStatus,
  limit: number,
  offset: number,
  signal?: AbortSignal,
): Promise<LanePageResult> {
  const params = new URLSearchParams({
    application_status: status,
    limit: String(limit),
    offset: String(offset),
  });
  const response = await request(`${LANE_API}?${params}`, signal);
  if (response.kind !== "ok") return response;

  const parsed = parseLane(response.body, status);
  if (parsed === null) return { kind: "failed" };
  return parsed;
}

async function request(
  url: string,
  signal?: AbortSignal,
): Promise<{ kind: "ok"; body: unknown } | { kind: "signed-out" } | { kind: "failed" }> {
  let response: Response;
  try {
    response = await fetch(url, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "failed" };
  }

  if (response.status === 401) {
    forgetProfile();
    return { kind: "signed-out" };
  }
  if (response.status !== 200) return { kind: "failed" };

  try {
    return { kind: "ok", body: await response.json() };
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "failed" };
  }
}

function parseBoard(value: unknown): { email: string; lanes: NoticeLane[] } | null {
  if (!isRecord(value)) return null;
  const email = parseEmail(value.email);
  if (email === null || !Array.isArray(value.lanes) || value.lanes.length !== NOTICE_STATUSES.length) {
    return null;
  }

  const lanes: NoticeLane[] = [];
  for (let index = 0; index < NOTICE_STATUSES.length; index += 1) {
    const expected = NOTICE_STATUSES[index].label;
    const lane = parseLaneRecord(value.lanes[index], expected);
    if (lane === null) return null;
    lanes.push(lane);
  }
  return { email, lanes };
}

function parseLane(value: unknown, expected: NoticeStatus): LanePageResult | null {
  if (!isRecord(value)) return null;
  const email = parseEmail(value.email);
  const lane = parseLaneRecord(value, expected);
  if (email === null || lane === null) return null;
  return { kind: "ready", email, status: lane.status, notices: lane.notices, limit: lane.limit };
}

function parseLaneRecord(value: unknown, expected: NoticeStatus): NoticeLane | null {
  if (!isRecord(value)) return null;
  if (parseStatus(value.status) !== expected) return null;
  if (!Array.isArray(value.notices)) return null;
  if (typeof value.limit !== "number" || !Number.isInteger(value.limit) || value.limit < 1) return null;
  if (value.notices.length > value.limit) return null;

  const notices: NoticeCard[] = [];
  for (const item of value.notices) {
    const notice = parseCard(item);
    if (notice === null) return null;
    notices.push(notice);
  }

  return { status: expected, tone: toneFor(expected), notices, limit: value.limit };
}

function parseCard(value: unknown): NoticeCard | null {
  if (!isRecord(value)) return null;
  if (typeof value.id !== "string" || !GUID.test(value.id)) return null;
  const company = parseCompany(value.company);
  if (company === null) return null;
  const role = parseRole(value.role);
  if (role === undefined) return null;
  const sentAt = parseSentAt(value.sentAt);
  if (sentAt === null) return null;
  return { id: value.id, company, role, sentAt };
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
