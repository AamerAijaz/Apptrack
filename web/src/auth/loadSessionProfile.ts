const SESSION_API = "/api/session";
const MAX_PICTURE_LENGTH = 2048;

export const REMEMBERED_PROFILE_KEY = "apptrack.session-profile";
export const REMEMBERED_PROFILE_MAX_AGE_MS = 24 * 60 * 60 * 1000;

export type RememberedProfile = {
  email: string;
  picture: string | null;
  savedAt: number;
};

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

export async function loadLandingSession(signal?: AbortSignal): Promise<SessionProfile> {
  const remembered = readRememberedProfile();
  if (remembered !== null && rememberedProfileIsFresh(remembered)) {
    return { kind: "signed-in", email: remembered.email, picture: remembered.picture };
  }

  const result = await loadSessionProfile(signal);
  if (result.kind === "signed-in") {
    rememberProfile({ email: result.email, picture: result.picture });
  } else {
    forgetProfile();
  }
  return result;
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

export function readRememberedProfile(): RememberedProfile | null {
  try {
    const raw = localStorage.getItem(REMEMBERED_PROFILE_KEY);
    if (raw === null) return null;
    return parseRemembered(JSON.parse(raw));
  } catch {
    return null;
  }
}

export function rememberProfile(profile: { email: string; picture: string | null }, savedAt = Date.now()): void {
  const remembered: RememberedProfile = { email: profile.email, picture: profile.picture, savedAt };
  try {
    localStorage.setItem(REMEMBERED_PROFILE_KEY, JSON.stringify(remembered));
  } catch (err) {
    // Error setting item to localStorage.
    console.error("ERROR: failed to set item to localStorage", err);
  }
}

export function rememberedProfileIsFresh(profile: RememberedProfile, now = Date.now()): boolean {
  const age = now - profile.savedAt;
  return age >= 0 && age < REMEMBERED_PROFILE_MAX_AGE_MS;
}

function parseRemembered(value: unknown): RememberedProfile | null {
  const profile = parseProfile(value);
  if (profile === null || typeof value !== "object" || value === null) return null;
  const savedAt = (value as { savedAt?: unknown }).savedAt;
  if (typeof savedAt !== "number" || !Number.isFinite(savedAt)) return null;
  return { email: profile.email, picture: profile.picture, savedAt };
}

export function forgetProfile(): void {
  try {
    localStorage.removeItem(REMEMBERED_PROFILE_KEY);
  } catch (err) {
    console.error("ERROR: failed to remove item from localStorage", err);
  }
}
