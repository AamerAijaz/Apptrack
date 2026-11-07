import { loadNotices } from "./loadNotices";

afterEach(() => {
  vi.restoreAllMocks();
});

test("the only request is GET /api/notices as JSON", async () => {
  const cookie = vi.spyOn(document, "cookie", "get");
  const fetchSpy = mockJson(200, { email: "ada@gmail.com", notices: [] });

  await loadNotices();

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

test("401 is signed out, even when the body is not JSON", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("nope", { status: 401 }));

  await expect(loadNotices()).resolves.toEqual({ kind: "signed-out" });
});

test("200 with an empty notices array is an empty inbox", async () => {
  mockJson(200, { email: "ada@gmail.com", notices: [] });

  await expect(loadNotices()).resolves.toEqual({
    kind: "empty",
    email: "ada@gmail.com",
  });
});

test("200 with notices groups them in status-list order without sorting", async () => {
  mockJson(200, {
    email: "ada@gmail.com",
    notices: [
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab01",
        status: "Rejected",
        sentAt: "2026-11-05T09:00:00Z",
        company: "Closed Co",
        role: null,
      }),
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab02",
        status: "Application Sent",
        sentAt: "2026-11-01T09:00:00Z",
        company: "Earlier Co",
      }),
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab03",
        status: "Interviewing",
        sentAt: "2026-11-03T14:22:05Z",
      }),
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab04",
        status: "Application Sent",
        sentAt: "2026-11-04T09:00:00Z",
        company: "Later Co",
      }),
    ],
  });

  const result = await loadNotices();

  expect(result.kind).toBe("ready");
  if (result.kind !== "ready") return;
  expect(result.email).toBe("ada@gmail.com");
  expect(result.groups.map((group) => group.label)).toEqual([
    "Application Sent",
    "Interviewing",
    "Rejected",
  ]);
  expect(result.groups.map((group) => group.tone)).toEqual(["sent", "interviewing", "rejected"]);
  expect(result.groups[0].notices.map((item) => item.company)).toEqual(["Earlier Co", "Later Co"]);
  expect(result.groups[1].notices.map((item) => item.id)).toEqual([
    "0192f3a4-5b6c-7d8e-9f01-23456789ab03",
  ]);
  expect(result.groups[1].notices[0].sentAt.toISOString()).toBe("2026-11-03T14:22:05.000Z");
  expect(result.groups[1].notices[0].role).toBe("Data Analyst");
  expect(result.groups[2].notices[0].role).toBeNull();
});

test.each([
  ["another status", 500, { email: "ada@gmail.com", notices: [] }],
  ["a missing email", 200, { notices: [] }],
  ["a blank email", 200, { email: "  ", notices: [] }],
  ["a notices object", 200, { email: "ada@gmail.com", notices: {} }],
  ["an empty company", 200, { email: "ada@gmail.com", notices: [notice({ company: "  " })] }],
  ["an empty role", 200, { email: "ada@gmail.com", notices: [notice({ role: "" })] }],
  ["a missing role", 200, { email: "ada@gmail.com", notices: [notice({ role: undefined })] }],
  ["an unknown status", 200, { email: "ada@gmail.com", notices: [notice({ status: "Hired" })] }],
  ["a bad date", 200, { email: "ada@gmail.com", notices: [notice({ sentAt: "2026-02-31T00:00:00Z" })] }],
  ["a non-UTC date", 200, { email: "ada@gmail.com", notices: [notice({ sentAt: "2026-11-03T14:22:05+00:00" })] }],
  ["an uppercase id", 200, { email: "ada@gmail.com", notices: [notice({ id: "0192F3A4-5B6C-7D8E-9F01-23456789ABCD" })] }],
])("%s fails the whole response", async (_name, status, body) => {
  mockJson(status, body);

  await expect(loadNotices()).resolves.toEqual({ kind: "failed" });
});

test("a non-JSON body fails", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("not-json", { status: 200 }));

  await expect(loadNotices()).resolves.toEqual({ kind: "failed" });
});

test("a network error fails", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));

  await expect(loadNotices()).resolves.toEqual({ kind: "failed" });
});

test("an aborted request is not turned into a failed load", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new DOMException("aborted", "AbortError"));

  await expect(loadNotices()).rejects.toMatchObject({ name: "AbortError" });
});

test("one unknown status drops the whole ready result", async () => {
  mockJson(200, {
    email: "ada@gmail.com",
    notices: [notice(), notice({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab09", status: "In review" })],
  });

  await expect(loadNotices()).resolves.toEqual({ kind: "failed" });
});

function notice(overrides: Record<string, unknown> = {}) {
  return {
    id: "0192f3a4-5b6c-7d8e-9f01-23456789abcd",
    company: "Northwind Freight",
    role: "Data Analyst",
    status: "Interviewing",
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
