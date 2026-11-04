export type ConnectGmailProps = {
  /** Visual weight only. Destination and label are fixed. */
  emphasis?: "primary" | "quiet";
};

const AUTH_START_PATH = "/api/auth";

export function ConnectGmail({ emphasis = "primary" }: ConnectGmailProps) {
  return (
    <a className={`connect connect--${emphasis}`} href={AUTH_START_PATH}>
      Connect Gmail
    </a>
  );
}
