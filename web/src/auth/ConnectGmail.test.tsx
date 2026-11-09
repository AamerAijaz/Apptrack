import { render, screen } from "@testing-library/react";
import { ConnectGmail } from "./ConnectGmail";

test("Connect is a link to the AppTrack sign-in start", () => {
  const fetchSpy = vi.spyOn(globalThis, "fetch");
  render(<ConnectGmail />);

  const link = screen.getByRole("link", { name: "Connect" });
  expect(link).toHaveAttribute("href", "/api/auth");
  expect(link.tagName).toBe("A");

  link.addEventListener("click", (event) => event.preventDefault());
  link.click();
  expect(fetchSpy).not.toHaveBeenCalled();
  fetchSpy.mockRestore();
});

test("a quiet Connect is a round control named Connect to Gmail", () => {
  render(<ConnectGmail emphasis="quiet" />);

  const link = screen.getByRole("link", { name: "Connect to Gmail" });
  expect(link).toHaveAttribute("href", "/api/auth");
  expect(link).toHaveAttribute("data-tooltip", "Connect to Gmail");
  expect(link).toHaveClass("connect--mark");
  expect(link).not.toHaveTextContent("Connect");
});
