import { NOTICE_STATUSES, groupByStatus, parseStatus } from "./statuses";

test("NOTICE_STATUSES is the glossary list in order", () => {
  expect(NOTICE_STATUSES).toEqual([
    { label: "Application Sent", tone: "sent" },
    { label: "Interviewing", tone: "interviewing" },
    { label: "Rejected", tone: "rejected" },
  ]);
});

test("parseStatus accepts only the three glossary strings", () => {
  expect(parseStatus("Application Sent")).toBe("Application Sent");
  expect(parseStatus("Interviewing")).toBe("Interviewing");
  expect(parseStatus("Rejected")).toBe("Rejected");
  expect(parseStatus("application sent")).toBeNull();
  expect(parseStatus("Submitted")).toBeNull();
  expect(parseStatus("")).toBeNull();
  expect(parseStatus(null)).toBeNull();
});

test("groupByStatus keeps three groups in list order and server order inside each", () => {
  const notices = [
    { id: "rejected-first", status: "Rejected" },
    { id: "sent-early", status: "Application Sent" },
    { id: "interviewing", status: "Interviewing" },
    { id: "sent-later", status: "Application Sent" },
  ];

  const groups = groupByStatus(notices);

  expect(groups).not.toBeNull();
  expect(groups?.map((group) => [group.label, group.tone])).toEqual([
    ["Application Sent", "sent"],
    ["Interviewing", "interviewing"],
    ["Rejected", "rejected"],
  ]);
  expect(groups?.[0].notices.map((notice) => notice.id)).toEqual(["sent-early", "sent-later"]);
  expect(groups?.[1].notices.map((notice) => notice.id)).toEqual(["interviewing"]);
  expect(groups?.[2].notices.map((notice) => notice.id)).toEqual(["rejected-first"]);
});

test("groupByStatus returns three empty groups for an empty list", () => {
  const groups = groupByStatus([]);

  expect(groups?.map((group) => group.notices)).toEqual([[], [], []]);
});

test("groupByStatus rejects an unknown status for the whole list", () => {
  expect(groupByStatus([{ id: "bad", status: "Hired" }])).toBeNull();
  expect(
    groupByStatus([
      { id: "ok", status: "Rejected" },
      { id: "bad", status: "In review" },
    ]),
  ).toBeNull();
});
