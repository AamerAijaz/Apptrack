import { useCallback, useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import { ConnectGmail } from "../auth/ConnectGmail";
import { Masthead, useMastheadSession } from "../Masthead";
import {
  addNotice as postNotice,
  getNotices,
  loadBoard,
  moveNotice as patchNotice,
  NOTICES_PATH,
  type AddNoticeResult,
  type BoardResult,
  type HandDraft,
  type NoticeCard,
  type NoticeLane,
} from "./loadNotices";
import { parseStatus, type NoticeStatus, type NoticeTone } from "./statuses";

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

type AddDraft =
  | { kind: "closed" }
  | {
      kind: "open";
      status: NoticeStatus;
      key: string;
      company: string;
      role: string;
      phase: "editing" | "saving" | "rejected" | "retired" | "failed";
    };

export function useNoticesPage(): NoticesPageState & {
  loadMore?: (status: NoticeStatus) => void;
  addNotice?: (draft: HandDraft) => Promise<AddNoticeResult>;
  moveNotice?: (id: string, status: NoticeStatus) => void;
} {
  const [state, setState] = useState<NoticesPageState>({ kind: "loading" });
  const stateRef = useRef(state);
  const abortRef = useRef<AbortController | null>(null);
  const latestMove = useRef(new Map<string, number>());

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

  const submitNotice = useCallback((draft: HandDraft) => {
    return postNotice(draft, abortRef.current?.signal).then(
      (result) => {
        setState((latest) => {
          if (latest.kind !== "ready") return latest;
          if (result.kind === "signed-out") return { kind: "signed-out" };
          if (result.kind !== "stored") return latest;
          return prependCard(latest, result.status, result.notice);
        });
        return result;
      },
      (error: unknown) => {
        if (isAbortError(error)) return { kind: "failed" } satisfies AddNoticeResult;
        return { kind: "failed" } satisfies AddNoticeResult;
      },
    );
  }, []);

  const relocate = useCallback((id: string, status: NoticeStatus) => {
    const current = stateRef.current;
    if (current.kind !== "ready") return;
    const source = current.lanes.find((lane) => lane.notices.some((notice) => notice.id === id));
    const destination = current.lanes.find((lane) => lane.status === status);
    if (source === undefined || destination === undefined || source.status === status) return;

    const limit = Math.min(50, destination.notices.length + 1);
    const epoch = (latestMove.current.get(id) ?? 0) + 1;
    latestMove.current.set(id, epoch);
    const currentMove = () => latestMove.current.get(id) === epoch;
    patchNotice(id, status, abortRef.current?.signal).then(
      async (result) => {
        if (!currentMove()) return;
        if (result.kind === "signed-out") {
          setState({ kind: "signed-out" });
          return;
        }
        if (result.kind !== "stored" || result.status !== status) return;

        const showStored = () => {
          setState((latest) => {
            if (latest.kind !== "ready") return latest;
            return placeMovedCard(latest, id, status, result.notice);
          });
        };

        try {
          const page = await getNotices(status, limit, 0, abortRef.current?.signal);
          if (!currentMove()) return;
          if (page.kind === "signed-out") {
            setState({ kind: "signed-out" });
            return;
          }
          if (page.kind !== "ready" || page.status !== status) {
            showStored();
            return;
          }
          setState((latest) => {
            if (latest.kind !== "ready") return latest;
            return {
              ...latest,
              lanes: latest.lanes.map((lane) => {
                if (lane.status !== status) {
                  return lane.notices.some((notice) => notice.id === id)
                    ? { ...lane, notices: lane.notices.filter((notice) => notice.id !== id) }
                    : lane;
                }
                return {
                  ...lane,
                  phase: "idle",
                  notices: page.notices,
                  limit: page.limit,
                  more: page.notices.length === page.limit,
                };
              }),
            };
          });
        } catch (error: unknown) {
          if (isAbortError(error) || !currentMove()) return;
          showStored();
        }
      },
      (error: unknown) => {
        if (isAbortError(error)) return;
      },
    );
  }, []);

  return state.kind === "ready"
    ? { ...state, loadMore, addNotice: submitNotice, moveNotice: relocate }
    : state;
}

function placeMovedCard(
  state: Extract<NoticesPageState, { kind: "ready" }>,
  id: string,
  status: NoticeStatus,
  notice: NoticeCard,
): Extract<NoticesPageState, { kind: "ready" }> {
  return {
    ...state,
    lanes: state.lanes.map((lane) => {
      const without = lane.notices.filter((item) => item.id !== id);
      if (lane.status !== status) return { ...lane, notices: without };
      return { ...lane, phase: "idle", notices: [notice, ...without] };
    }),
  };
}

export function NoticesPage() {
  const session = useMastheadSession();
  const state = useNoticesPage();
  const loadMore = state.kind === "ready" ? state.loadMore : undefined;
  const submitNotice = state.kind === "ready" ? state.addNotice : undefined;
  const moveNotice = state.kind === "ready" ? state.moveNotice : undefined;
  const [adding, setAdding] = useState<AddDraft>({ kind: "closed" });

  function open(status: NoticeStatus) {
    setAdding({
      kind: "open",
      status,
      key: crypto.randomUUID(),
      company: "",
      role: "",
      phase: "editing",
    });
  }

  return (
    <>
      <Masthead session={session} signedOut={state.kind === "signed-out"} />
      <main className="notice-sheet">
        <h1>Application Notices</h1>
        <NoticesBody
          state={state}
          onLoadMore={loadMore}
          onAdd={submitNotice ? open : undefined}
          onMove={moveNotice}
        />
        {adding.kind === "open" && submitNotice ? (
          <AddNoticeDialog
            draft={adding}
            onDraft={setAdding}
            onSubmit={submitNotice}
            onClose={() => setAdding({ kind: "closed" })}
          />
        ) : null}
      </main>
    </>
  );
}

function NoticesBody({
  state,
  onLoadMore,
  onAdd,
  onMove,
}: {
  state: NoticesPageState;
  onLoadMore?: (status: NoticeStatus) => void;
  onAdd?: (status: NoticeStatus) => void;
  onMove?: (id: string, status: NoticeStatus) => void;
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
          <NoticesBoard lanes={state.lanes} onLoadMore={onLoadMore} onAdd={onAdd} onMove={onMove} />
        </>
      );
    }
  }
}

type CardDrag = {
  notice: NoticeCard;
  tone: NoticeTone;
  from: NoticeStatus;
  x: number;
  y: number;
  width: number;
  over: NoticeStatus | null;
};

function NoticesBoard({
  lanes,
  onLoadMore,
  onAdd,
  onMove,
}: {
  lanes: readonly LaneState[];
  onLoadMore?: (status: NoticeStatus) => void;
  onAdd?: (status: NoticeStatus) => void;
  onMove?: (id: string, status: NoticeStatus) => void;
}) {
  const drag = useCardDrag(onMove);

  return (
    <div className={drag.current === null ? "notice-board" : "notice-board is-dragging"}>
      {lanes.map((lane) => (
        <NoticeRow
          key={lane.status}
          lane={lane}
          onLoadMore={onLoadMore}
          onAdd={onAdd}
          dropHere={drag.current !== null && drag.current.over === lane.status && drag.current.from !== lane.status}
          draggingId={drag.current?.notice.id}
          onCardPointerDown={onMove === undefined ? undefined : drag.begin}
        />
      ))}
      {drag.current === null ? null : <DraggedCard drag={drag.current} />}
    </div>
  );
}

function useCardDrag(onMove?: (id: string, status: NoticeStatus) => void) {
  const [current, setCurrent] = useState<CardDrag | null>(null);
  const onMoveRef = useRef(onMove);
  onMoveRef.current = onMove;
  const teardownRef = useRef<(() => void) | null>(null);

  const begin = useCallback((event: ReactPointerEvent<HTMLElement>, notice: NoticeCard, status: NoticeStatus, tone: NoticeTone) => {
    if (event.button !== 0 || teardownRef.current !== null) return;
    const origin = event.currentTarget.getBoundingClientRect();
    const startX = event.clientX;
    const startY = event.clientY;
    const offsetX = startX - origin.left;
    const offsetY = startY - origin.top;
    let moved = false;
    let over: NoticeStatus | null = null;

    const place = (clientX: number, clientY: number) => {
      over = laneUnder(clientX, clientY);
      setCurrent({
        notice,
        tone,
        from: status,
        x: clientX - offsetX,
        y: clientY - offsetY,
        width: origin.width,
        over,
      });
    };

    const teardown = () => {
      document.removeEventListener("pointermove", follow);
      document.removeEventListener("pointerup", finish);
      document.removeEventListener("pointercancel", finish);
      document.body.classList.remove("is-notice-dragging");
      teardownRef.current = null;
    };

    const follow = (ev: PointerEvent) => {
      if (!moved && Math.hypot(ev.clientX - startX, ev.clientY - startY) < 6) return;
      moved = true;
      document.body.classList.add("is-notice-dragging");
      place(ev.clientX, ev.clientY);
    };

    const finish = (ev: PointerEvent) => {
      teardown();
      if (moved && ev.type === "pointerup") {
        const released = laneUnder(ev.clientX, ev.clientY);
        if (released !== null && released !== status) onMoveRef.current?.(notice.id, released);
      }
      setCurrent(null);
    };

    teardownRef.current = teardown;
    document.addEventListener("pointermove", follow);
    document.addEventListener("pointerup", finish);
    document.addEventListener("pointercancel", finish);
  }, []);

  useEffect(() => {
    return () => {
      teardownRef.current?.();
    };
  }, []);

  return { current, begin };
}

function laneUnder(x: number, y: number): NoticeStatus | null {
  const hit = document.elementFromPoint(x, y);
  if (hit === null) return null;
  return parseStatus(hit.closest("[data-notice-lane]")?.getAttribute("data-notice-lane"));
}

function DraggedCard({ drag }: { drag: CardDrag }) {
  return (
    <article
      className={`notice-card notice-card--${drag.tone} notice-card-float`}
      style={{ left: drag.x, top: drag.y, width: drag.width }}
      aria-hidden="true"
    >
      <CardCopy notice={drag.notice} status={drag.from} />
    </article>
  );
}

function NoticeRow({
  lane,
  onLoadMore,
  onAdd,
  dropHere,
  draggingId,
  onCardPointerDown,
}: {
  lane: LaneState;
  onLoadMore?: (status: NoticeStatus) => void;
  onAdd?: (status: NoticeStatus) => void;
  dropHere: boolean;
  draggingId?: string;
  onCardPointerDown?: (event: ReactPointerEvent<HTMLElement>, notice: NoticeCard, status: NoticeStatus, tone: NoticeTone) => void;
}) {
  const headingId = `notice-lane-${lane.tone}`;
  const hasMore = lane.more && lane.phase === "idle";
  const sentinel = useNearEnd(hasMore, () => onLoadMore?.(lane.status));

  return (
    <section
      className={`notice-lane notice-lane--${lane.tone}${dropHere ? " is-drop" : ""}`}
      aria-labelledby={headingId}
      data-notice-lane={lane.status}
    >
      <div className="notice-lane-head">
        <h2 id={headingId}>
          {lane.status}{" "}
          <span className="notice-count">
            {lane.notices.length}
            <span className="visually-hidden">
              {lane.notices.length === 1 ? " notice" : " notices"}
            </span>
          </span>
        </h2>
        {onAdd ? (
          <button
            type="button"
            className="notice-add"
            aria-label={`Add notice to ${lane.status}`}
            onClick={() => onAdd(lane.status)}
          >
            Add notice
          </button>
        ) : null}
      </div>
      <ul className="notice-lane-track">
        {lane.notices.map((notice) => (
          <li key={notice.id}>
            <NoticeCardView
              notice={notice}
              tone={lane.tone}
              status={lane.status}
              lifted={draggingId === notice.id}
              onPointerDown={
                onCardPointerDown === undefined
                  ? undefined
                  : (event) => onCardPointerDown(event, notice, lane.status, lane.tone)
              }
            />
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
  lifted,
  onPointerDown,
}: {
  notice: NoticeCard;
  tone: NoticeTone;
  status: NoticeStatus;
  lifted: boolean;
  onPointerDown?: (event: ReactPointerEvent<HTMLElement>) => void;
}) {
  return (
    <article
      className={`notice-card notice-card--${tone}${lifted ? " is-lifted" : ""}`}
      onPointerDown={
        onPointerDown === undefined
          ? undefined
          : (event) => {
              const fromHandle =
                event.target instanceof Element && event.target.closest(".notice-drag") !== null;
              if (event.pointerType === "touch" && !fromHandle) return;
              onPointerDown(event);
            }
      }
    >
      {onPointerDown === undefined ? null : (
        <button type="button" className="notice-drag" aria-label={`Move ${notice.company}`}>
          Move
        </button>
      )}
      <CardCopy notice={notice} status={status} />
    </article>
  );
}

function CardCopy({ notice, status }: { notice: NoticeCard; status: NoticeStatus }) {
  return (
    <>
      <p className="notice-company">{notice.company}</p>
      <p className="notice-role">{notice.role ?? "Role not named"}</p>
      <p className="notice-status">{status}</p>
      <p className="notice-sent">Sent {formatSentAt(notice.sentAt)}</p>
    </>
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

function prependCard(
  state: Extract<NoticesPageState, { kind: "ready" }>,
  status: NoticeStatus,
  notice: NoticeCard,
): NoticesPageState {
  return {
    ...state,
    lanes: state.lanes.map((lane) => {
      if (lane.status !== status) return lane;
      if (lane.notices.some((item) => item.id === notice.id)) return lane;
      return { ...lane, notices: [notice, ...lane.notices] };
    }),
  };
}

function AddNoticeDialog({
  draft,
  onDraft,
  onSubmit,
  onClose,
}: {
  draft: Extract<AddDraft, { kind: "open" }>;
  onDraft: (draft: AddDraft) => void;
  onSubmit: (draft: HandDraft) => Promise<AddNoticeResult>;
  onClose: () => void;
}) {
  const titleId = "add-notice-title";
  const saving = draft.phase === "saving";

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape" && !saving) onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose, saving]);

  async function submit() {
    if (saving) return;
    onDraft({ ...draft, phase: "saving" });
    const result = await onSubmit({
      status: draft.status,
      company: draft.company,
      role: draft.role,
      key: draft.key,
    });
    if (result.kind === "stored" || result.kind === "signed-out") {
      onClose();
      return;
    }
    if (result.kind === "rejected") onDraft({ ...draft, phase: "rejected" });
    else if (result.kind === "retired") onDraft({ ...draft, phase: "retired" });
    else onDraft({ ...draft, phase: "failed" });
  }

  return (
    <div className="notice-dialog-backdrop">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="notice-dialog"
      >
        <h2 id={titleId}>Add notice</h2>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <label>
            Company
            <input
              value={draft.company}
              onChange={(event) => onDraft({ ...draft, company: event.target.value })}
              autoFocus
              disabled={saving}
            />
          </label>
          <label>
            Role
            <input
              value={draft.role}
              onChange={(event) => onDraft({ ...draft, role: event.target.value })}
              disabled={saving}
            />
          </label>
          <label>
            Status
            <input value={draft.status} readOnly />
          </label>
          {draft.phase === "rejected" ? <p role="alert">A notice needs a company.</p> : null}
          {draft.phase === "retired" ? (
            <p role="alert">That notice was removed.</p>
          ) : null}
          {draft.phase === "failed" ? <p role="alert">Couldn't add the notice. Try again.</p> : null}
          <div className="notice-dialog-actions">
            <button type="button" className="notice-retry" onClick={onClose} disabled={saving}>
              Cancel
            </button>
            {draft.phase === "retired" ? (
              <button
                type="button"
                className="notice-add"
                onClick={() =>
                  onDraft({
                    ...draft,
                    key: crypto.randomUUID(),
                    phase: "editing",
                  })
                }
              >
                Try again
              </button>
            ) : (
              <button type="submit" className="notice-add" disabled={saving}>
                Add
              </button>
            )}
          </div>
        </form>
      </div>
    </div>
  );
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
