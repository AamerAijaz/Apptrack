import { addNotice, getNotices, loadBoard } from "./loadNotices";
import { REMEMBERED_PROFILE_KEY } from "../auth/loadSessionProfile";

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

test("add posts the draft and returns the stored card", async () => {
  const fetchSpy = mockJson(200, {
    status: "Interviewing",
    notice: card({ company: "Acme", role: "Engineer" }),
  });

  const result = await addNotice({
    status: "Interviewing",
    company: "Acme",
    role: "Engineer",
    key: "6f1c1d1e-2b3a-4c5d-8e9f-0a1b2c3d4e5f",
  });

  expect(fetchSpy).toHaveBeenCalledWith(
    "/api/notices",
    expect.objectContaining({
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify({
        status: "Interviewing",
        company: "Acme",
        role: "Engineer",
        key: "6f1c1d1e-2b3a-4c5d-8e9f-0a1b2c3d4e5f",
      }),
    }),
  );
  expect(result.kind).toBe("stored");
  if (result.kind !== "stored") return;
  expect(result.status).toBe("Interviewing");
  expect(result.notice.company).toBe("Acme");
});

test("a blank company is rejected and a removed key is retired", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(new Response(null, { status: 400 }));
  await expect(
    addNotice({ status: "Accepted", company: " ", role: "", key: "6f1c1d1e-2b3a-4c5d-8e9f-0a1b2c3d4e5f" }),
  ).resolves.toEqual({ kind: "rejected" });

  vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(new Response(null, { status: 409 }));
  await expect(
    addNotice({ status: "Accepted", company: "Acme", role: "", key: "6f1c1d1e-2b3a-4c5d-8e9f-0a1b2c3d4e5f" }),
  ).resolves.toEqual({ kind: "retired" });
});

test("the board request is GET /api/notices as JSON", async () => {
  const cookie = vi.spyOn(document, "cookie", "get");
  const fetchSpy = mockJson(200, board());

  await loadBoard();

  expect(fetchSpy).toHaveBeenCalledTimes(1);
  expect(fetchSpy).toHaveBeenCalledWith(
    "/api/notices",
    expect.objectContaining({
      method: "GET",
      headers: { Accept: "application/json" },
    }),
  );
  expect(cookie).not.toHaveBeenCalled();
});

test("401 is signed out and forgets the remembered profile", async () => {
  localStorage.setItem(
    REMEMBERED_PROFILE_KEY,
    JSON.stringify({ email: "ada@gmail.com", picture: null, savedAt: Date.now() }),
  );
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("nope", { status: 401 }));

  await expect(loadBoard()).resolves.toEqual({ kind: "signed-out" });
  expect(localStorage.getItem(REMEMBERED_PROFILE_KEY)).toBeNull();
});

test("200 with four empty lanes is a ready board", async () => {
  mockJson(200, board());

  const result = await loadBoard();

  expect(result.kind).toBe("ready");
  if (result.kind !== "ready") return;
  expect(result.email).toBe("ada@gmail.com");
  expect(result.lanes.map((lane) => lane.status)).toEqual([
    "Application Sent",
    "Interviewing",
    "Accepted",
    "Rejected",
  ]);
  expect(result.lanes.every((lane) => lane.notices.length === 0)).toBe(true);
});

test("cards stay in lane order and drop a status field", async () => {
  mockJson(200, board({
    0: [
      card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab02", company: "Earlier Co", sentAt: "2026-11-04T16:40:00Z" }),
      card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab01", company: "Later Co", role: null, sentAt: "2026-11-01T08:15:00Z" }),
    ],
  }));

  const result = await loadBoard();
  if (result.kind !== "ready") throw new Error("expected a board");

  expect(result.lanes[0].notices.map((notice) => notice.company)).toEqual(["Earlier Co", "Later Co"]);
  expect(result.lanes[0].notices[1].role).toBeNull();
  expect(result.lanes[0].notices[1].sentAt.toISOString()).toBe("2026-11-01T08:15:00.000Z");
});

test("a lane page asks for that status, limit, and offset", async () => {
  const fetchSpy = mockJson(200, {
    email: "ada@gmail.com",
    status: "Accepted",
    limit: 12,
    notices: [card({ company: "Offered Co" })],
  });

  const result = await getNotices("Accepted", 12, 24);

  expect(fetchSpy).toHaveBeenCalledWith(
    "/api/notices/lane?application_status=Accepted&limit=12&offset=24",
    expect.anything(),
  );
  if (result.kind !== "ready") throw new Error("expected a page");
  expect(result.notices.map((notice) => notice.company)).toEqual(["Offered Co"]);
  expect(result.limit).toBe(12);
});

test.each([
  ["notices that are not a list", boardWithNotices({})],
  ["a missing email", { lanes: emptyLanes() }],
  ["the wrong lane order", board({ swap: true })],
  ["an unknown status", board({ status: "Hired" })],
  ["a bad date", board({ sentAt: "2026-02-31T00:00:00Z" })],
])("%s fails the whole response", async (_name, body) => {
  mockJson(200, body);
  await expect(loadBoard()).resolves.toEqual({ kind: "failed" });
});

test("a lane page longer than its limit fails that page", async () => {
  mockJson(200, {
    email: "ada@gmail.com",
    status: "Accepted",
    limit: 1,
    notices: [card(), card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab09" })],
  });
  await expect(getNotices("Accepted", 1, 0)).resolves.toEqual({ kind: "failed" });
});

test("a non-200 lane page fails that page only", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 400 }));
  await expect(getNotices("Rejected", 12, 0)).resolves.toEqual({ kind: "failed" });
});

test("unmount is not this module's job, but abort throws", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new DOMException("aborted", "AbortError"));
  await expect(loadBoard()).rejects.toMatchObject({ name: "AbortError" });
});

function boardWithNotices(notices: unknown) {
  const lanes = emptyLanes();
  return { email: "ada@gmail.com", lanes: [{ ...lanes[0], notices }, ...lanes.slice(1)] };
}

function emptyLanes() {
  return ["Application Sent", "Interviewing", "Accepted", "Rejected"].map((status) => ({
    status,
    limit: 12,
    notices: [] as ReturnType<typeof card>[],
  }));
}

function board(overrides: Record<string, unknown> = {}) {
  if ("lanes" in overrides || "swap" in overrides || "status" in overrides || "sentAt" in overrides) {
    if (overrides.lanes !== undefined) return { email: "ada@gmail.com", ...overrides };
    const lanes = emptyLanes();
    if (overrides.swap === true) lanes.reverse();
    if (typeof overrides.status === "string") lanes[0].status = overrides.status;
    if (typeof overrides.sentAt === "string") {
      lanes[0].notices = [card({ sentAt: overrides.sentAt })];
    }
    return { email: "ada@gmail.com", lanes };
  }

  const lanes = emptyLanes();
  for (const [index, notices] of Object.entries(overrides)) {
    lanes[Number(index)].notices = notices as ReturnType<typeof card>[];
  }
  return { email: "ada@gmail.com", lanes };
}

function card(overrides: Record<string, unknown> = {}) {
  return {
    id: "0192f3a4-5b6c-7d8e-9f01-23456789abcd",
    company: "Northwind Freight",
    role: "Data Analyst",
    sentAt: "2026-11-03T14:22:05Z",
    ...overrides,
  };
}

function mockJson(status: number, body: unknown) {
  return vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    }),
  );
}
