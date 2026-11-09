export type ConnectGmailProps = {
  /** Visual weight only. Destination stays the sign-in start. */
  emphasis?: "primary" | "quiet";
};

const AUTH_START_PATH = "/api/auth";
const CONNECT_LABEL = "Connect to Gmail";

export function ConnectGmail({ emphasis = "primary" }: ConnectGmailProps) {
  if (emphasis === "quiet") {
    return (
      <a
        className="connect connect--quiet connect--mark"
        href={AUTH_START_PATH}
        aria-label={CONNECT_LABEL}
        data-tooltip={CONNECT_LABEL}
      >
        <ConnectIcon />
      </a>
    );
  }

  return (
    <a className="connect connect--primary" href={AUTH_START_PATH}>
      Connect
    </a>
  );
}

function ConnectIcon() {
  return (
    <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true" focusable="false">
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"
      />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"
      />
    </svg>
  );
}
