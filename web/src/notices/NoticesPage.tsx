import { useEffect, useState } from "react";
import { ConnectGmail } from "../auth/ConnectGmail";
import { loadNotices, NOTICES_PATH, type Notice, type NoticesResult } from "./loadNotices";
import type { NoticeTone, StatusGroup } from "./statuses";

const EMPTY_COPY =
  "No Application Notices yet. AppTrack reads new mail from now on. Notices appear here as companies write to you.";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

export type NoticesPageState = { kind: "loading" } | NoticesResult;

export function useNoticesPage(): NoticesPageState {
  const [state, setState] = useState<NoticesPageState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;

    loadNotices(controller.signal).then(
      (result) => {
        if (!cancelled) setState(result);
      },
      (error: unknown) => {
        if (cancelled || isAbortError(error)) return;
        setState({ kind: "failed" });
      },
    );

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, []);

  return state;
}

export function NoticesPage() {
  const state = useNoticesPage();
  const email = state.kind === "empty" || state.kind === "ready" ? state.email : null;

  return (
    <>
      <header className="masthead">
        <a className="wordmark" href="/">
          AppTrack
        </a>
        {email ? <p className="signed-in">Signed in as {email}</p> : null}
      </header>
      <main className="notice-sheet">
        <h1>Application Notices</h1>
        <NoticesBody state={state} />
      </main>
    </>
  );
}

function NoticesBody({ state }: { state: NoticesPageState }) {
  switch (state.kind) {
    case "loading":
      return <p role="status">Loading your notices.</p>;
    case "signed-out":
      return (
        <div className="notice-message">
          <p>You're not signed in.</p>
          <ConnectGmail />
        </div>
      );
    case "failed":
      return (
        <div className="notice-message">
          <p>Couldn't load your notices.</p>
          <a href={NOTICES_PATH}>Try again</a>
        </div>
      );
    case "empty":
      return <p className="notice-empty-copy">{EMPTY_COPY}</p>;
    case "ready":
      return (
        <div className="notice-groups">
          {state.groups.map((group) => (
            <NoticeGroup key={group.label} group={group} />
          ))}
        </div>
      );
  }
}

function NoticeGroup({ group }: { group: StatusGroup<Notice> }) {
  const headingId = `notice-group-${group.tone}`;
  return (
    <section className={`notice-group notice-group--${group.tone}`} aria-labelledby={headingId}>
      <h2 id={headingId}>{group.label}</h2>
      {group.notices.length === 0 ? (
        <p className="notice-none">None yet</p>
      ) : (
        <ul className="notice-list">
          {group.notices.map((notice) => (
            <li key={notice.id}>
              <NoticeCard notice={notice} tone={group.tone} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function NoticeCard({ notice, tone }: { notice: Notice; tone: NoticeTone }) {
  return (
    <article className={`notice-card notice-card--${tone}`}>
      <p className="notice-company">{notice.company}</p>
      <p className="notice-role">{notice.role ?? "Role not named"}</p>
      <p className="notice-status">{notice.status}</p>
      <p className="notice-sent">Sent {formatSentAt(notice.sentAt)}</p>
    </article>
  );
}

function formatSentAt(sentAt: Date): string {
  const day = sentAt.getUTCDate();
  const month = MONTHS[sentAt.getUTCMonth()];
  const year = sentAt.getUTCFullYear();
  const hours = String(sentAt.getUTCHours()).padStart(2, "0");
  const minutes = String(sentAt.getUTCMinutes()).padStart(2, "0");
  return `${day} ${month} ${year}, ${hours}:${minutes} UTC`;
}

function isAbortError(error: unknown): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "name" in error &&
    error.name === "AbortError"
  );
}
