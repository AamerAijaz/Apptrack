import { act, fireEvent, render, screen } from "@testing-library/react";
import { REMEMBERED_PROFILE_KEY, REMEMBERED_PROFILE_MAX_AGE_MS } from "./auth/loadSessionProfile";
import { Landing } from "./Landing";

const PICTURE = "https://lh3.googleusercontent.com/a/ada";

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

test("holds the masthead until the session is known", async () => {
  let resolveFetch: ((response: Response) => void) | undefined;
  vi.spyOn(globalThis, "fetch").mockImplementation(
    () =>
      new Promise((resolve) => {
        resolveFetch = resolve;
      }),
  );

  const { container } = render(<Landing />);

  expect(screen.queryByRole("link", { name: /connect/i })).not.toBeInTheDocument();
  expect(screen.queryByText("Read-only Gmail access. AppTrack never sends mail.")).not.toBeInTheDocument();
  expect(container.querySelector(".profile--pending")).not.toBeNull();

  resolveFetch?.(jsonResponse(200, { email: "ada@gmail.com", picture: PICTURE }));

  const photo = await screen.findByRole("img", { name: "ada@gmail.com" });
  expect(photo).toHaveAttribute("src", PICTURE);
  expect(photo.closest("a")).toHaveAttribute("href", "/notices");
  expect(screen.getByRole("link", { name: "Notices" })).toHaveAttribute("href", "/notices");
  expect(screen.queryByRole("link", { name: /connect/i })).not.toBeInTheDocument();
  expect(screen.queryByText("Read-only Gmail access. AppTrack never sends mail.")).not.toBeInTheDocument();
  const stored = JSON.parse(localStorage.getItem(REMEMBERED_PROFILE_KEY) ?? "null");
  expect(stored).toMatchObject({ email: "ada@gmail.com", picture: PICTURE });
  expect(typeof stored.savedAt).toBe("number");
});

test("a fresh remembered picture does not ask the server", async () => {
  localStorage.setItem(
    REMEMBERED_PROFILE_KEY,
    JSON.stringify({ email: "ada@gmail.com", picture: PICTURE, savedAt: Date.now() }),
  );
  const fetchSpy = vi.spyOn(globalThis, "fetch");

  render(<Landing />);

  expect(screen.getByRole("img", { name: "ada@gmail.com" })).toHaveAttribute("src", PICTURE);
  expect(screen.getByRole("link", { name: "Notices" })).toHaveAttribute("href", "/notices");
  expect(screen.queryByRole("link", { name: /connect/i })).not.toBeInTheDocument();
  await act(async () => {
    await Promise.resolve();
  });
  expect(fetchSpy).not.toHaveBeenCalled();
});

test("a stale remembered picture is checked and cleared when the session is gone", async () => {
  localStorage.setItem(
    REMEMBERED_PROFILE_KEY,
    JSON.stringify({
      email: "ada@gmail.com",
      picture: PICTURE,
      savedAt: Date.now() - REMEMBERED_PROFILE_MAX_AGE_MS,
    }),
  );
  const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<Landing />);

  expect(screen.getByRole("img", { name: "ada@gmail.com" })).toHaveAttribute("src", PICTURE);
  expect(await screen.findByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
  expect(fetchSpy).toHaveBeenCalledWith("/api/session", expect.anything());
  expect(localStorage.getItem(REMEMBERED_PROFILE_KEY)).toBeNull();
});

test("a signed-out session leaves both Connect Gmail links", async () => {
  const cookie = vi.spyOn(document, "cookie", "get");
  const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<Landing />);

  expect(await screen.findByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^Connect$/ })).toBeInTheDocument();
  expect(fetchSpy).toHaveBeenCalledWith("/api/session", expect.anything());
  expect(cookie).not.toHaveBeenCalled();

  const link = screen.getByRole("link", { name: "Connect to Gmail" });
  link.addEventListener("click", (event) => event.preventDefault());
  link.click();
  expect(fetchSpy).toHaveBeenCalledTimes(1);
});

test("a failed session read leaves Connect Gmail", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));

  render(<Landing />);

  expect(await screen.findByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^Connect$/ })).toBeInTheDocument();
});

test("a session without a picture shows the email's first letter and hides Connect Gmail", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "aamerasim45@gmail.com", picture: null }),
  );

  render(<Landing />);

  const mark = await screen.findByRole("link", { name: "aamerasim45@gmail.com" });
  expect(mark).toHaveAttribute("href", "/notices");
  expect(mark).toHaveTextContent("A");
  expect(screen.getByRole("link", { name: "Notices" })).toHaveAttribute("href", "/notices");
  expect(screen.queryByText("aamerasim45@gmail.com")).not.toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
  expect(screen.queryByRole("link", { name: /connect/i })).not.toBeInTheDocument();
  expect(screen.queryByText("Read-only Gmail access. AppTrack never sends mail.")).not.toBeInTheDocument();
});

test("an http picture is not used as the image", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "ada@gmail.com", picture: "http://lh3.googleusercontent.com/a/ada" }),
  );

  render(<Landing />);

  expect(await screen.findByRole("link", { name: "ada@gmail.com" })).toHaveTextContent("A");
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("a picture that fails to load falls back to the email's first letter", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "ada@gmail.com", picture: PICTURE }),
  );

  render(<Landing />);

  const photo = await screen.findByRole("img", { name: "ada@gmail.com" });
  fireEvent.error(photo);

  expect(screen.getByRole("link", { name: "ada@gmail.com" })).toHaveTextContent("A");
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("the connect failure stays on the signed-out landing page", async () => {
  window.history.replaceState(null, "", "/?connect=failed");
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<Landing />);

  expect(await screen.findByText("Gmail couldn't be connected. Try again.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Connect to Gmail" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^Connect$/ })).toBeInTheDocument();
});

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
