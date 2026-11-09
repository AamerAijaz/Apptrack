const SESSION_API = "/api/session";
const MAX_PICTURE_LENGTH = 2048;

export type SessionProfile =
  | { kind: "signed-out" }
  | { kind: "signed-in"; email: string; picture: string | null };

export async function loadSessionProfile(signal?: AbortSignal): Promise<SessionProfile> {
  let response: Response;
  try {
    response = await fetch(SESSION_API, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "signed-out" };
  }

  if (response.status !== 200) return { kind: "signed-out" };

  let body: unknown;
  try {
    body = await response.json();
  } catch (error) {
    if (isAbortError(error)) throw error;
    return { kind: "signed-out" };
  }

  const parsed = parseProfile(body);
  if (parsed === null) return { kind: "signed-out" };
  return { kind: "signed-in", email: parsed.email, picture: parsed.picture };
}

function parseProfile(value: unknown): { email: string; picture: string | null } | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as { email?: unknown; picture?: unknown };
  if (typeof record.email !== "string") return null;
  const email = record.email.trim();
  if (email.length === 0) return null;

  return { email, picture: parsePicture(record.picture) };
}

function parsePicture(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const picture = value.trim();
  if (!picture.startsWith("https://") || picture.length > MAX_PICTURE_LENGTH) return null;
  return picture;
}

export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}
