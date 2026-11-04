import { render, screen } from "@testing-library/react";
import { ConnectGmail } from "./ConnectGmail";

test("Connect Gmail is a link to the AppTrack sign-in start", () => {
  const fetchSpy = vi.spyOn(globalThis, "fetch");
  render(<ConnectGmail />);

  const link = screen.getByRole("link", { name: "Connect Gmail" });
  expect(link).toHaveAttribute("href", "/api/auth");
  expect(link.tagName).toBe("A");

  link.addEventListener("click", (event) => event.preventDefault());
  link.click();
  expect(fetchSpy).not.toHaveBeenCalled();
  fetchSpy.mockRestore();
});

test("a quiet Connect Gmail still links to the same sign-in start", () => {
  render(<ConnectGmail emphasis="quiet" />);

  expect(screen.getByRole("link", { name: "Connect Gmail" })).toHaveAttribute(
    "href",
    "/api/auth",
  );
});
