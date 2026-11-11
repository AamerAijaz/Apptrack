import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { REMEMBERED_PROFILE_KEY } from "../auth/loadSessionProfile";
import { Landing } from "../Landing";
import { NoticesPage } from "./NoticesPage";

const PICTURE = "https://lh3.googleusercontent.com/a/ada";

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

test("shows a loading status until the request settles", async () => {
  const pending: Array<{ url: string; resolve: (response: Response) => void }> = [];
  vi.spyOn(globalThis, "fetch").mockImplementation((input) => {
    const url = String(input);
    return new Promise((resolve) => {
      pending.push({ url, resolve });
    });
  });

  const { container } = render(<NoticesPage />);

  expect(screen.getByRole("status")).toHaveTextContent("Loading your notices.");
  expect(container.querySelector(".profile--pending")).not.toBeNull();

  for (const request of pending) {
    request.resolve(request.url === "/api/session" ? sessionResponse() : jsonResponse(200, board()));
  }

  const photo = await screen.findByRole("img", { name: "ada@gmail.com" });
  expect(photo).toHaveAttribute("src", PICTURE);
  expect(photo.closest("a")).toHaveAttribute("href", "/notices");
  expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

test("401 shows the signed-out message and Connect", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<NoticesPage />);

  expect(await screen.findByText("You're not signed in.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Connect" })).toHaveAttribute("href", "/api/auth");
  expect(screen.getByRole("link", { name: "Connect to Gmail" })).toHaveAttribute("href", "/api/auth");
  expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("a remembered picture gives way when the board is signed out", async () => {
  localStorage.setItem(
    REMEMBERED_PROFILE_KEY,
    JSON.stringify({ email: "ada@gmail.com", picture: PICTURE, savedAt: Date.now() }),
  );
  let resolveFetch: ((response: Response) => void) | undefined;
  vi.spyOn(globalThis, "fetch").mockImplementation(
    () =>
      new Promise((resolve) => {
        resolveFetch = resolve;
      }),
  );

  render(<NoticesPage />);

  expect(screen.getByRole("img", { name: "ada@gmail.com" })).toHaveAttribute("src", PICTURE);

  resolveFetch?.(new Response(null, { status: 401 }));

  expect(await screen.findByText("You're not signed in.")).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(localStorage.getItem(REMEMBERED_PROFILE_KEY)).toBeNull();
});

test("a failed load offers a link back to /notices", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));

  render(<NoticesPage />);

  expect(await screen.findByText("Couldn't load your notices.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Try again" })).toHaveAttribute("href", "/notices");
});

test("an empty inbox shows the empty copy and four lanes", async () => {
  mockNotices(board());

  render(<NoticesPage />);

  expect(
    await screen.findByText(
      "No Application Notices yet. AppTrack reads new mail from now on. Notices appear here as companies write to you.",
    ),
  ).toBeInTheDocument();
  expect(screen.getByRole("img", { name: "ada@gmail.com" })).toHaveAttribute("src", PICTURE);
  expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
  expect(screen.queryByText("None yet")).not.toBeInTheDocument();
  expect(screen.queryByRole("article")).not.toBeInTheDocument();
  expect(screen.getByRole("region", { name: "Application Sent 0 notices" })).toBeInTheDocument();
});

test("ready notices render four lanes and keep server order inside a lane", async () => {
  const fetchSpy = mockNotices(
    board({
      0: [
        card({
          id: "0192f3a4-5b6c-7d8e-9f01-23456789ab02",
          company: "Earlier Co",
          role: "Analyst",
          sentAt: "2026-11-01T08:15:00Z",
        }),
        card({
          id: "0192f3a4-5b6c-7d8e-9f01-23456789ab04",
          company: "Later Co",
          role: "Writer",
          sentAt: "2026-11-04T16:40:00Z",
        }),
      ],
      1: [card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab03", company: "Northwind Freight", role: null })],
    }),
  );

  render(<NoticesPage />);

  expect(await screen.findByRole("img", { name: "ada@gmail.com" })).toHaveAttribute("src", PICTURE);
  expect(screen.getAllByRole("heading").map((heading) => heading.textContent?.replace(/\s+/g, " ").trim())).toEqual([
    "Application Notices",
    "Application Sent 2 notices",
    "Interviewing 1 notice",
    "Accepted 0 notices",
    "Rejected 0 notices",
  ]);

  const sent = screen.getByRole("region", { name: "Application Sent 2 notices" });
  const sentCards = within(sent).getAllByRole("article");
  expect(sentCards.map((item) => item.querySelector(".notice-company")?.textContent)).toEqual([
    "Earlier Co",
    "Later Co",
  ]);
  expect(sentCards[0]).toHaveClass("notice-card", "notice-card--sent");
  expect(within(sentCards[0]).getByText("Analyst")).toBeInTheDocument();
  expect(within(sentCards[0]).getByText("Application Sent")).toBeInTheDocument();
  expect(within(sentCards[0]).getByText("Sent 1 Nov 2026, 08:15 UTC")).toBeInTheDocument();

  const interviewing = screen.getByRole("region", { name: "Interviewing 1 notice" });
  const interviewCard = within(interviewing).getByRole("article");
  expect(interviewCard).toHaveClass("notice-card", "notice-card--interviewing");
  expect(within(interviewCard).getByText("Role not named")).toBeInTheDocument();

  expect(within(screen.getByRole("region", { name: "Accepted 0 notices" })).queryByRole("article")).not.toBeInTheDocument();
  expect(within(screen.getByRole("region", { name: "Rejected 0 notices" })).queryByRole("article")).not.toBeInTheDocument();
  expect(screen.queryByText("None yet")).not.toBeInTheDocument();
  expect(fetchSpy).toHaveBeenCalledWith("/api/notices", expect.anything());
});

test("a full lane asks for the next offset when its end is visible", async () => {
  const notices = Array.from({ length: 12 }, (_, index) =>
    card({
      id: `0192f3a4-5b6c-7d8e-9f01-23456789ab${index.toString(16).padStart(2, "0")}`,
      company: `Company ${index}`,
    }),
  );
  let calls = 0;
  vi.spyOn(globalThis, "fetch").mockImplementation((input) => {
    const url = String(input);
    if (url === "/api/session") return Promise.resolve(sessionResponse());
    calls += 1;
    if (url.startsWith("/api/notices/lane")) {
      return Promise.resolve(
        jsonResponse(200, {
          email: "ada@gmail.com",
          status: "Application Sent",
          limit: 12,
          notices: [card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab99", company: "Older Co" })],
        }),
      );
    }
    return Promise.resolve(jsonResponse(200, board({ 0: notices })));
  });

  const observed: IntersectionObserverCallback[] = [];
  vi.stubGlobal(
    "IntersectionObserver",
    class {
      constructor(callback: IntersectionObserverCallback) {
        observed.push(callback);
      }
      observe() {}
      disconnect() {}
      unobserve() {}
      takeRecords() {
        return [];
      }
    },
  );

  render(<NoticesPage />);
  expect(await screen.findByText("Company 0")).toBeInTheDocument();

  await act(async () => {
    observed[0]?.([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
  });

  expect(await screen.findByText("Older Co")).toBeInTheDocument();
  expect(calls).toBe(2);
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

test("adding a notice from a stage opens a popup and puts the new card first", async () => {
  const posted: string[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
    const url = String(input);
    if (url === "/api/session") return Promise.resolve(sessionResponse());
    if (init?.method === "POST") {
      posted.push(String(init.body));
      return Promise.resolve(
        jsonResponse(200, {
          status: "Interviewing",
          notice: card({
            id: "0192f3a4-5b6c-7d8e-9f01-23456789ab10",
            company: "Acme",
            role: "Engineer",
            sentAt: "2026-11-10T11:11:00.000Z",
          }),
        }),
      );
    }
    return Promise.resolve(
      jsonResponse(
        200,
        board({
          1: [card({ id: "0192f3a4-5b6c-7d8e-9f01-23456789ab03", company: "Northwind Freight", role: null })],
        }),
      ),
    );
  });

  render(<NoticesPage />);
  await screen.findByRole("img", { name: "ada@gmail.com" });
  fireEvent.click(screen.getByRole("button", { name: "Add notice to Interviewing" }));

  const dialog = screen.getByRole("dialog", { name: "Add notice" });
  expect(within(dialog).getByLabelText("Status")).toHaveValue("Interviewing");
  fireEvent.change(within(dialog).getByLabelText("Company"), { target: { value: "Acme" } });
  fireEvent.change(within(dialog).getByLabelText("Role"), { target: { value: "Engineer" } });
  fireEvent.click(within(dialog).getByRole("button", { name: "Add" }));

  const lane = await screen.findByRole("region", { name: "Interviewing 2 notices" });
  expect(within(lane).getAllByRole("article")[0]).toHaveTextContent("Acme");
  expect(within(lane).getAllByRole("article")[1]).toHaveTextContent("Northwind Freight");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(JSON.parse(posted[0])).toMatchObject({
    company: "Acme",
    role: "Engineer",
    status: "Interviewing",
  });
  expect(JSON.parse(posted[0]).key).toEqual(expect.any(String));
});

test("dropping a card on another stage moves it and reloads that stage in recorded order", async () => {
  const acme = "0192f3a4-5b6c-7d8e-9f01-23456789ab01";
  const beta = "0192f3a4-5b6c-7d8e-9f01-23456789ab02";
  const northwind = "0192f3a4-5b6c-7d8e-9f01-23456789ab03";
  const patches: { url: string; body: string }[] = [];
  const laneGets: string[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
    const url = String(input);
    if (url === "/api/session") return Promise.resolve(sessionResponse());
    if (init?.method === "PATCH") {
      patches.push({ url, body: String(init.body) });
      return Promise.resolve(
        jsonResponse(200, {
          status: "Interviewing",
          notice: card({ id: acme, company: "Acme", role: "Engineer" }),
        }),
      );
    }
    if (url.startsWith("/api/notices/lane")) {
      laneGets.push(url);
      return Promise.resolve(
        jsonResponse(200, {
          email: "ada@gmail.com",
          status: "Interviewing",
          limit: 2,
          notices: [
            card({ id: northwind, company: "Northwind Freight", role: null }),
            card({ id: acme, company: "Acme", role: "Engineer" }),
          ],
        }),
      );
    }
    return Promise.resolve(
      jsonResponse(
        200,
        board({
          0: [
            card({ id: beta, company: "Beta", role: null }),
            card({ id: acme, company: "Acme", role: "Engineer" }),
          ],
          1: [card({ id: northwind, company: "Northwind Freight", role: null })],
        }),
      ),
    );
  });

  render(<NoticesPage />);
  const sent = await screen.findByRole("region", { name: "Application Sent 2 notices" });
  const interviewing = screen.getByRole("region", { name: "Interviewing 1 notice" });
  const acmeCard = within(sent).getByText("Acme").closest("article");
  expect(acmeCard).not.toBeNull();

  releaseCardOn(acmeCard!, sent);
  expect(patches).toHaveLength(0);

  releaseCardOn(acmeCard!, interviewing);

  expect(await within(interviewing).findByText("Acme")).toBeInTheDocument();
  expect(within(interviewing).getAllByRole("article").map((article) => article.textContent)).toEqual([
    expect.stringContaining("Northwind Freight"),
    expect.stringContaining("Acme"),
  ]);
  expect(within(sent).queryByText("Acme")).not.toBeInTheDocument();
  expect(within(sent).getByText("Beta")).toBeInTheDocument();
  expect(patches).toEqual([{ url: `/api/notices/${acme}`, body: JSON.stringify({ status: "Interviewing" }) }]);
  expect(laneGets).toEqual(["/api/notices/lane?application_status=Interviewing&limit=2&offset=0"]);
});

test("a later drop wins when an earlier reload is still in flight", async () => {
  const acme = "0192f3a4-5b6c-7d8e-9f01-23456789ab01";
  let releaseInterviewing: () => void = () => {};
  const interviewingGate = new Promise<void>((resolve) => {
    releaseInterviewing = resolve;
  });
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url === "/api/session") return sessionResponse();
    if (init?.method === "PATCH") {
      const status = JSON.parse(String(init.body)).status as string;
      return jsonResponse(200, { status, notice: card({ id: acme, company: "Acme" }) });
    }
    if (url.startsWith("/api/notices/lane")) {
      const status = new URL(url, "http://localhost").searchParams.get("application_status");
      if (status === "Interviewing") await interviewingGate;
      return jsonResponse(200, {
        email: "ada@gmail.com",
        status,
        limit: 1,
        notices: [card({ id: acme, company: "Acme" })],
      });
    }
    return jsonResponse(200, board({ 0: [card({ id: acme, company: "Acme" })] }));
  });

  render(<NoticesPage />);
  const sent = await screen.findByRole("region", { name: "Application Sent 1 notice" });
  const interviewing = screen.getByRole("region", { name: "Interviewing 0 notices" });
  const accepted = screen.getByRole("region", { name: "Accepted 0 notices" });
  const acmeCard = within(sent).getByText("Acme").closest("article");

  releaseCardOn(acmeCard!, interviewing);
  releaseCardOn(acmeCard!, accepted);
  releaseInterviewing();

  expect(await within(accepted).findByText("Acme")).toBeInTheDocument();
  expect(within(interviewing).queryByText("Acme")).not.toBeInTheDocument();
  expect(within(sent).queryByText("Acme")).not.toBeInTheDocument();
});

test("a slower lane reload keeps a notice another move already placed", async () => {
  const acme = "0192f3a4-5b6c-7d8e-9f01-23456789ab01";
  const beta = "0192f3a4-5b6c-7d8e-9f01-23456789ab02";
  let interviewingLoads = 0;
  let releaseFirst: () => void = () => {};
  const firstLane = new Promise<void>((resolve) => {
    releaseFirst = resolve;
  });
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url === "/api/session") return sessionResponse();
    if (init?.method === "PATCH") {
      const id = url.split("/").at(-1);
      const status = JSON.parse(String(init.body)).status as string;
      const company = id === beta ? "Beta" : "Acme";
      return jsonResponse(200, { status, notice: card({ id, company }) });
    }
    if (url.startsWith("/api/notices/lane")) {
      const status = new URL(url, "http://localhost").searchParams.get("application_status");
      if (status === "Interviewing") {
        interviewingLoads += 1;
        if (interviewingLoads === 1) {
          await firstLane;
          return jsonResponse(200, {
            email: "ada@gmail.com",
            status,
            limit: 1,
            notices: [card({ id: acme, company: "Acme" })],
          });
        }
        return jsonResponse(200, {
          email: "ada@gmail.com",
          status,
          limit: 1,
          notices: [card({ id: beta, company: "Beta" })],
        });
      }
    }
    return jsonResponse(
      200,
      board({
        0: [card({ id: acme, company: "Acme" }), card({ id: beta, company: "Beta" })],
      }),
    );
  });

  render(<NoticesPage />);
  const sent = await screen.findByRole("region", { name: "Application Sent 2 notices" });
  const interviewing = screen.getByRole("region", { name: "Interviewing 0 notices" });
  const acmeCard = within(sent).getByText("Acme").closest("article");
  const betaCard = within(sent).getByText("Beta").closest("article");

  releaseCardOn(acmeCard!, interviewing);
  await waitFor(() => expect(interviewingLoads).toBe(1));
  releaseCardOn(betaCard!, interviewing);

  expect(await within(interviewing).findByText("Beta")).toBeInTheDocument();
  releaseFirst();
  await waitFor(() => expect(within(interviewing).getByText("Acme")).toBeInTheDocument());
  expect(within(interviewing).getByText("Beta")).toBeInTheDocument();
  expect(within(sent).queryByText("Acme")).not.toBeInTheDocument();
  expect(within(sent).queryByText("Beta")).not.toBeInTheDocument();
});

test("a failed move leaves the card in its stage", async () => {
  let patches = 0;
  vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
    if (String(input) === "/api/session") return Promise.resolve(sessionResponse());
    if (init?.method === "PATCH") {
      patches += 1;
      return Promise.resolve(new Response(null, { status: 500 }));
    }
    return Promise.resolve(
      jsonResponse(200, board({ 0: [card({ company: "Acme" })] })),
    );
  });

  render(<NoticesPage />);
  const sent = await screen.findByRole("region", { name: "Application Sent 1 notice" });
  const rejected = screen.getByRole("region", { name: "Rejected 0 notices" });
  const acmeCard = within(sent).getByText("Acme").closest("article");
  releaseCardOn(acmeCard!, rejected);

  await waitFor(() => expect(patches).toBe(1));
  expect(within(sent).getByText("Acme")).toBeInTheDocument();
  expect(within(rejected).queryByText("Acme")).not.toBeInTheDocument();
});

test("a rejected notice stays in the popup", async () => {
  vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
    if (String(input) === "/api/session") return Promise.resolve(sessionResponse());
    if (init?.method === "POST") return Promise.resolve(new Response(null, { status: 400 }));
    return Promise.resolve(jsonResponse(200, board()));
  });

  render(<NoticesPage />);
  await screen.findByRole("img", { name: "ada@gmail.com" });
  fireEvent.click(screen.getByRole("button", { name: "Add notice to Accepted" }));
  fireEvent.click(screen.getByRole("button", { name: "Add" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("A notice needs a company.");
  expect(screen.getByRole("dialog", { name: "Add notice" })).toBeInTheDocument();
  expect(screen.getByLabelText("Status")).toHaveValue("Accepted");
});

test("the landing page shows the connect failure only for the exact query", async () => {
  window.history.replaceState(null, "", "/?connect=failed");
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));
  const { unmount } = render(<Landing />);

  expect(await screen.findByText("Gmail couldn't be connected. Try again.")).toBeInTheDocument();
  const hero = screen.getByRole("region", { name: "Every Job Application, straight from your inbox." });
  const connects = within(hero).getAllByRole("link", { name: /^Connect$/ });
  expect(connects).toHaveLength(1);
  expect(
    hero.innerHTML.indexOf("Gmail couldn't be connected. Try again."),
  ).toBeLessThan(hero.innerHTML.indexOf("Connect"));
  unmount();

  window.history.replaceState(null, "", "/?connect=failed&next=1");
  render(<Landing />);
  expect(screen.queryByText("Gmail couldn't be connected. Try again.")).not.toBeInTheDocument();
  expect(await screen.findByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^Connect$/ })).toBeInTheDocument();
});

function emptyLanes() {
  return ["Application Sent", "Interviewing", "Accepted", "Rejected"].map((status) => ({
    status,
    limit: 12,
    notices: [] as ReturnType<typeof card>[],
  }));
}

function board(filled: Record<number, ReturnType<typeof card>[]> = {}) {
  const lanes = emptyLanes();
  for (const [index, notices] of Object.entries(filled)) {
    lanes[Number(index)].notices = notices;
  }
  return { email: "ada@gmail.com", lanes };
}

function card(overrides: Record<string, unknown> = {}) {
  return {
    id: "0192f3a4-5b6c-7d8e-9f01-23456789abcd",
    company: "Northwind Freight",
    role: "Data Analyst" as string | null,
    sentAt: "2026-11-03T14:22:05Z",
    ...overrides,
  };
}

function releaseCardOn(card: Element, lane: Element) {
  const point = (type: string, x: number, y: number) =>
    new MouseEvent(type, { bubbles: true, clientX: x, clientY: y, button: 0 });
  document.elementFromPoint = () => lane;
  act(() => {
    card.dispatchEvent(point("pointerdown", 4, 4));
    card.dispatchEvent(point("pointermove", 40, 16));
    card.dispatchEvent(point("pointerup", 40, 16));
  });
}

function mockNotices(body: unknown) {
  return vi.spyOn(globalThis, "fetch").mockImplementation((input) => {
    if (String(input) === "/api/session") return Promise.resolve(sessionResponse());
    return Promise.resolve(jsonResponse(200, body));
  });
}

function sessionResponse() {
  return jsonResponse(200, { email: "ada@gmail.com", picture: PICTURE });
}

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
