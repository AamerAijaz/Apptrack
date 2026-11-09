import { fireEvent, render, screen } from "@testing-library/react";
import { Landing } from "./Landing";

const PICTURE = "https://lh3.googleusercontent.com/a/ada";

afterEach(() => {
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});

test("keeps Connect Gmail until the session is known", async () => {
  let resolveFetch: ((response: Response) => void) | undefined;
  vi.spyOn(globalThis, "fetch").mockImplementation(
    () =>
      new Promise((resolve) => {
        resolveFetch = resolve;
      }),
  );

  render(<Landing />);

  expect(screen.getAllByRole("link", { name: "Connect Gmail" })).toHaveLength(2);
  expect(screen.getByText("Read-only Gmail access. AppTrack never sends mail.")).toBeInTheDocument();

  resolveFetch?.(jsonResponse(200, { email: "ada@gmail.com", picture: PICTURE }));

  const photo = await screen.findByRole("img", { name: "ada@gmail.com" });
  expect(photo).toHaveAttribute("src", PICTURE);
  expect(photo.closest("a")).toHaveAttribute("href", "/notices");
  expect(screen.queryByRole("link", { name: "Connect Gmail" })).not.toBeInTheDocument();
  expect(screen.queryByText("Read-only Gmail access. AppTrack never sends mail.")).not.toBeInTheDocument();
});

test("a signed-out session leaves both Connect Gmail links", async () => {
  const cookie = vi.spyOn(document, "cookie", "get");
  const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<Landing />);

  expect(await screen.findAllByRole("link", { name: "Connect Gmail" })).toHaveLength(2);
  expect(fetchSpy).toHaveBeenCalledWith("/api/session", expect.anything());
  expect(cookie).not.toHaveBeenCalled();

  const link = screen.getAllByRole("link", { name: "Connect Gmail" })[0];
  link.addEventListener("click", (event) => event.preventDefault());
  link.click();
  expect(fetchSpy).toHaveBeenCalledTimes(1);
});

test("a failed session read leaves Connect Gmail", async () => {
  vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));

  render(<Landing />);

  expect(await screen.findAllByRole("link", { name: "Connect Gmail" })).toHaveLength(2);
});

test("a session without a picture shows the email and hides Connect Gmail", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "ada@gmail.com", picture: null }),
  );

  render(<Landing />);

  expect(await screen.findByText("ada@gmail.com")).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "Connect Gmail" })).not.toBeInTheDocument();
  expect(screen.queryByText("Read-only Gmail access. AppTrack never sends mail.")).not.toBeInTheDocument();
});

test("an http picture is not used as the image", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "ada@gmail.com", picture: "http://lh3.googleusercontent.com/a/ada" }),
  );

  render(<Landing />);

  expect(await screen.findByText("ada@gmail.com")).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("a picture that fails to load falls back to the email", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    jsonResponse(200, { email: "ada@gmail.com", picture: PICTURE }),
  );

  render(<Landing />);

  const photo = await screen.findByRole("img", { name: "ada@gmail.com" });
  fireEvent.error(photo);

  expect(screen.getByText("ada@gmail.com")).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("the connect failure stays on the signed-out landing page", async () => {
  window.history.replaceState(null, "", "/?connect=failed");
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 401 }));

  render(<Landing />);

  expect(await screen.findByText("Gmail couldn't be connected. Try again.")).toBeInTheDocument();
  expect(screen.getAllByRole("link", { name: "Connect Gmail" })).toHaveLength(2);
});

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
