import type { PointerEvent as ReactPointerEvent } from "react";
import type { NoticeCard } from "./loadNotices";
import type { NoticeStatus, NoticeTone } from "./statuses";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

export function NoticeCardView({
  notice,
  tone,
  status,
  lifted = false,
  onPointerDown,
}: {
  notice: NoticeCard;
  tone: NoticeTone;
  status: NoticeStatus;
  lifted?: boolean;
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
      <CardCopy notice={notice} status={status} />
    </article>
  );
}

export function CardCopy({ notice, status }: { notice: NoticeCard; status: NoticeStatus }) {
  return (
    <>
      <p className="notice-company">{notice.company}</p>
      <p className="notice-role">{notice.role ?? "Role not named"}</p>
      <p className="notice-status">{status}</p>
      <p className="notice-sent">Sent {formatSentAt(notice.sentAt)}</p>
    </>
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
