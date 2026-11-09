import { useCallback, useEffect, useRef, useState } from "react";
import { ConnectGmail } from "../auth/ConnectGmail";
import { Masthead, useMastheadSession } from "../Masthead";
import {
  getNotices,
  loadBoard,
  NOTICES_PATH,
  type BoardResult,
  type NoticeCard,
  type NoticeLane,
} from "./loadNotices";
import type { NoticeStatus, NoticeTone } from "./statuses";

const EMPTY_COPY =
  "No Application Notices yet. AppTrack reads new mail from now on. Notices appear here as companies write to you.";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

type LanePhase = "idle" | "loading" | "failed";

type LaneState = NoticeLane & {
  readonly more: boolean;
  readonly phase: LanePhase;
};

export type NoticesPageState =
  | { kind: "loading" }
  | { kind: "signed-out" }
  | { kind: "failed" }
  | { kind: "ready"; email: string; lanes: readonly LaneState[] };

export function useNoticesPage(): NoticesPageState & { loadMore?: (status: NoticeStatus) => void } {
  const [state, setState] = useState<NoticesPageState>({ kind: "loading" });
  const stateRef = useRef(state);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    stateRef.current = state;
  });

  useEffect(() => {
    const controller = new AbortController();
    abortRef.current = controller;
    let cancelled = false;

    loadBoard(controller.signal).then(
      (result) => {
        if (cancelled) return;
        setState(boardToState(result));
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

  const loadMore = useCallback((status: NoticeStatus) => {
    const current = stateRef.current;
    if (current.kind !== "ready") return;
    const lane = current.lanes.find((item) => item.status === status);
    if (lane === undefined || lane.phase === "loading") return;
    if (!lane.more && lane.phase !== "failed") return;

    const offset = lane.notices.length;
    const limit = lane.limit;
    stateRef.current = markLane(current, status, { phase: "loading" });
    setState((latest) => (latest.kind === "ready" ? markLane(latest, status, { phase: "loading" }) : latest));

    getNotices(status, limit, offset, abortRef.current?.signal).then(
      (result) => {
        setState((latest) => {
          if (latest.kind !== "ready") return latest;
          if (result.kind === "signed-out") return { kind: "signed-out" };
          if (result.kind === "failed" || result.status !== status) {
            return markLane(latest, status, { phase: "failed" });
          }
          const existing = latest.lanes.find((item) => item.status === status);
          if (existing === undefined) return latest;
          const seen = new Set(existing.notices.map((notice) => notice.id));
          const added = result.notices.filter((notice) => !seen.has(notice.id));
          return markLane(latest, status, {
            phase: "idle",
            notices: [...existing.notices, ...added],
            limit: result.limit,
            more: result.notices.length === result.limit,
          });
        });
      },
      (error: unknown) => {
        if (isAbortError(error)) return;
        setState((latest) => (latest.kind === "ready" ? markLane(latest, status, { phase: "failed" }) : latest));
      },
    );
  }, []);

  return state.kind === "ready" ? { ...state, loadMore } : state;
}

export function NoticesPage() {
  const session = useMastheadSession();
  const state = useNoticesPage();
  const loadMore = state.kind === "ready" ? state.loadMore : undefined;

  return (
    <>
      <Masthead session={session} signedOut={state.kind === "signed-out"} />
      <main className="notice-sheet">
        <h1>Application Notices</h1>
        <NoticesBody state={state} onLoadMore={loadMore} />
      </main>
    </>
  );
}

function NoticesBody({
  state,
  onLoadMore,
}: {
  state: NoticesPageState;
  onLoadMore?: (status: NoticeStatus) => void;
}) {
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
    case "ready": {
      const allEmpty = state.lanes.every((lane) => lane.notices.length === 0 && lane.phase === "idle");
      return (
        <>
          {allEmpty ? <p className="notice-empty-copy">{EMPTY_COPY}</p> : null}
          <div className="notice-board">
            {state.lanes.map((lane) => (
              <NoticeRow key={lane.status} lane={lane} onLoadMore={onLoadMore} />
            ))}
          </div>
        </>
      );
    }
  }
}

function NoticeRow({
  lane,
  onLoadMore,
}: {
  lane: LaneState;
  onLoadMore?: (status: NoticeStatus) => void;
}) {
  const headingId = `notice-lane-${lane.tone}`;
  const hasMore = lane.more && lane.phase === "idle";
  const sentinel = useNearEnd(hasMore, () => onLoadMore?.(lane.status));

  return (
    <section className={`notice-lane notice-lane--${lane.tone}`} aria-labelledby={headingId}>
      <h2 id={headingId}>
        {lane.status}{" "}
        <span className="notice-count">
          {lane.notices.length}
          <span className="visually-hidden">
            {lane.notices.length === 1 ? " notice" : " notices"}
          </span>
        </span>
      </h2>
      <ul className="notice-lane-track">
        {lane.notices.map((notice) => (
          <li key={notice.id}>
            <NoticeCardView notice={notice} tone={lane.tone} status={lane.status} />
          </li>
        ))}
        {lane.phase === "loading" ? (
          <li role="status">Loading</li>
        ) : null}
        {lane.phase === "failed" ? (
          <li>
            <button type="button" className="notice-retry" onClick={() => onLoadMore?.(lane.status)}>
              Try again
            </button>
          </li>
        ) : null}
        {hasMore ? <li ref={sentinel} className="notice-sentinel" aria-hidden="true" /> : null}
      </ul>
    </section>
  );
}

function NoticeCardView({
  notice,
  tone,
  status,
}: {
  notice: NoticeCard;
  tone: NoticeTone;
  status: NoticeStatus;
}) {
  return (
    <article className={`notice-card notice-card--${tone}`}>
      <p className="notice-company">{notice.company}</p>
      <p className="notice-role">{notice.role ?? "Role not named"}</p>
      <p className="notice-status">{status}</p>
      <p className="notice-sent">Sent {formatSentAt(notice.sentAt)}</p>
    </article>
  );
}

function useNearEnd(enabled: boolean, onNearEnd: () => void) {
  const ref = useRef<HTMLLIElement>(null);
  const onNearEndRef = useRef(onNearEnd);
  onNearEndRef.current = onNearEnd;

  useEffect(() => {
    const node = ref.current;
    if (!enabled || node === null) return;
    const root = node.parentElement;
    if (root === null || typeof IntersectionObserver === "undefined") return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) onNearEndRef.current();
      },
      { root, rootMargin: "0px 400px 0px 0px", threshold: 0 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [enabled]);

  return ref;
}

function boardToState(result: BoardResult): NoticesPageState {
  if (result.kind !== "ready") return result;
  return {
    kind: "ready",
    email: result.email,
    lanes: result.lanes.map((lane) => ({
      ...lane,
      phase: "idle" as const,
      more: lane.notices.length === lane.limit,
    })),
  };
}

function markLane(
  state: Extract<NoticesPageState, { kind: "ready" }>,
  status: NoticeStatus,
  patch: Partial<Pick<LaneState, "phase" | "notices" | "limit" | "more">>,
): NoticesPageState {
  return {
    ...state,
    lanes: state.lanes.map((lane) => (lane.status === status ? { ...lane, ...patch } : lane)),
  };
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
