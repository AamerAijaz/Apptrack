import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { NOTICE_STATUSES, parseStatus, toneFor } from "./statuses";

test("NOTICE_STATUSES matches the shared contract in order", () => {
  const path = join(dirname(fileURLToPath(import.meta.url)), "../../../contracts/notice-statuses.json");
  expect(NOTICE_STATUSES).toEqual(JSON.parse(readFileSync(path, "utf8")));
});

test("parseStatus accepts only the glossary strings", () => {
  expect(parseStatus("Application Sent")).toBe("Application Sent");
  expect(parseStatus("Interviewing")).toBe("Interviewing");
  expect(parseStatus("Accepted")).toBe("Accepted");
  expect(parseStatus("Rejected")).toBe("Rejected");
  expect(parseStatus("application sent")).toBeNull();
  expect(parseStatus("Submitted")).toBeNull();
  expect(parseStatus("Done")).toBeNull();
  expect(parseStatus("")).toBeNull();
  expect(parseStatus(null)).toBeNull();
});

test("toneFor follows the contract", () => {
  expect(toneFor("Accepted")).toBe("accepted");
  expect(toneFor("Interviewing")).toBe("interviewing");
});
