import { render, screen, within } from "@testing-library/react";
import { Landing } from "../Landing";
import { NoticesPage } from "./NoticesPage";

afterEach(() => {
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});

test("shows a loading status until the request settles", async () => {
  let resolveFetch: ((response: Response) => void) | undefined;
  vi.spyOn(globalThis, "fetch").mockImplementation(
    () =>
      new Promise((resolve) => {
        resolveFetch = resolve;
      }),
  );

  render(<NoticesPage />);

  expect(screen.getByRole("status")).toHaveTextContent("Loading your notices.");

  resolveFetch?.(
    jsonResponse(200, {
      email: "ada@gmail.com",
      notices: [],
    }),
  );

  expect(await screen.findByText("Signed in as ada@gmail.com")).toBeInTheDocument();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

test("401 shows the signed-out message and Connect Gmail", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<NoticesPage />);

  expect(await screen.findByText("You're not signed in.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Connect Gmail" })).toHaveAttribute("href", "/api/auth");
  expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
});

test("a failed load offers a link back to /notices", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));

  render(<NoticesPage />);

  expect(await screen.findByText("Couldn't load your notices.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Try again" })).toHaveAttribute("href", "/notices");
});

test("an empty inbox uses the empty copy and the signed-in masthead", async () => {
  mockNotices({ email: "ada@gmail.com", notices: [] });

  render(<NoticesPage />);

  expect(
    await screen.findByText(
      "No Application Notices yet. AppTrack reads new mail from now on. Notices appear here as companies write to you.",
    ),
  ).toBeInTheDocument();
  expect(screen.getByText("Signed in as ada@gmail.com")).toBeInTheDocument();
  expect(screen.queryByRole("article")).not.toBeInTheDocument();
});

test("ready notices render three tone groups, preserving server order", async () => {
  const cookie = vi.spyOn(document, "cookie", "get");
  const fetchSpy = mockNotices({
    email: "ada@gmail.com",
    notices: [
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab02",
        company: "Earlier Co",
        role: "Analyst",
        status: "Application Sent",
        sentAt: "2026-11-01T08:15:00Z",
      }),
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab03",
        company: "Northwind Freight",
        role: null,
        status: "Interviewing",
        sentAt: "2026-11-03T14:22:05Z",
      }),
      notice({
        id: "0192f3a4-5b6c-7d8e-9f01-23456789ab04",
        company: "Later Co",
        role: "Writer",
        status: "Application Sent",
        sentAt: "2026-11-04T16:40:00Z",
      }),
    ],
  });

  render(<NoticesPage />);

  expect(await screen.findByText("Signed in as ada@gmail.com")).toBeInTheDocument();
  expect(screen.getAllByRole("heading").map((heading) => heading.textContent)).toEqual([
    "Application Notices",
    "Application Sent",
    "Interviewing",
    "Rejected",
  ]);

  const sent = screen.getByRole("region", { name: "Application Sent" });
  const sentCards = within(sent).getAllByRole("article");
  expect(sentCards.map((card) => card.querySelector(".notice-company")?.textContent)).toEqual([
    "Earlier Co",
    "Later Co",
  ]);
  expect(sentCards[0]).toHaveClass("notice-card", "notice-card--sent");
  expect(within(sentCards[0]).getByText("Analyst")).toBeInTheDocument();
  expect(within(sentCards[0]).getByText("Application Sent")).toBeInTheDocument();
  expect(within(sentCards[0]).getByText("Sent 1 Nov 2026, 08:15 UTC")).toBeInTheDocument();

  const interviewing = screen.getByRole("region", { name: "Interviewing" });
  const interviewCard = within(interviewing).getByRole("article");
  expect(interviewCard).toHaveClass("notice-card", "notice-card--interviewing");
  expect(within(interviewCard).getByText("Northwind Freight")).toBeInTheDocument();
  expect(within(interviewCard).getByText("Role not named")).toBeInTheDocument();
  expect(within(interviewCard).getByText("Interviewing")).toBeInTheDocument();
  expect(within(interviewCard).getByText("Sent 3 Nov 2026, 14:22 UTC")).toBeInTheDocument();

  const rejected = screen.getByRole("region", { name: "Rejected" });
  expect(within(rejected).getByText("None yet")).toBeInTheDocument();
  expect(within(rejected).queryByRole("article")).not.toBeInTheDocument();

  expect(fetchSpy).toHaveBeenCalledTimes(1);
  expect(fetchSpy).toHaveBeenCalledWith("/api/notices", expect.anything());
  expect(String(fetchSpy.mock.calls[0][0])).toBe("/api/notices");
  expect(cookie).not.toHaveBeenCalled();
});

test("unmount aborts the notices request", () => {
  let signal: AbortSignal | undefined;
  vi.spyOn(globalThis, "fetch").mockImplementation((_input, init) => {
    signal = init?.signal ?? undefined;
    return new Promise(() => {});
  });

  const { unmount } = render(<NoticesPage />);
  expect(signal?.aborted).toBe(false);

  unmount();

  expect(signal?.aborted).toBe(true);
});

test("the landing page shows the connect failure only for the exact query", async () => {
  window.history.replaceState(null, "", "/?connect=failed");
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));
  const { unmount } = render(<Landing />);

  expect(await screen.findByText("Gmail couldn't be connected. Try again.")).toBeInTheDocument();
  const hero = screen.getByRole("region", { name: "Every Job Application, straight from your inbox." });
  const connects = within(hero).getAllByRole("link", { name: "Connect Gmail" });
  expect(connects).toHaveLength(1);
  expect(
    hero.innerHTML.indexOf("Gmail couldn't be connected. Try again."),
  ).toBeLessThan(hero.innerHTML.indexOf("Connect Gmail"));
  unmount();

  window.history.replaceState(null, "", "/?connect=failed&next=1");
  render(<Landing />);
  expect(screen.queryByText("Gmail couldn't be connected. Try again.")).not.toBeInTheDocument();
  expect(await screen.findAllByRole("link", { name: "Connect Gmail" })).toHaveLength(2);
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

function mockNotices(body: unknown) {
  return vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse(200, body));
}

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
