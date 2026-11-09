import { useEffect, useState } from "react";
import { ConnectGmail } from "./auth/ConnectGmail";
import {
  forgetProfile,
  isAbortError,
  loadLandingSession,
  readRememberedProfile,
  type SessionProfile,
} from "./auth/loadSessionProfile";

export type MastheadSession = { kind: "pending" } | SessionProfile;

export function useMastheadSession(): MastheadSession {
  const [state, setState] = useState<MastheadSession>(() => {
    const remembered = readRememberedProfile();
    return remembered === null ? { kind: "pending" } : { kind: "signed-in", ...remembered };
  });

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;

    loadLandingSession(controller.signal).then(
      (result) => {
        if (!cancelled) setState(result);
      },
      (error: unknown) => {
        if (cancelled || isAbortError(error)) return;
        forgetProfile();
        setState({ kind: "signed-out" });
      },
    );

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, []);

  return state;
}

export function Masthead({
  session,
  signedOut = false,
}: {
  session: MastheadSession;
  signedOut?: boolean;
}) {
  const showOut = signedOut || session.kind === "signed-out";
  const showIn = !showOut && session.kind === "signed-in";

  return (
    <header className="masthead">
      <a className="wordmark" href="/">
        AppTrack
      </a>
      {showIn ? (
        <SignedInMark email={session.email} picture={session.picture} />
      ) : showOut ? (
        <ConnectGmail emphasis="quiet" />
      ) : (
        <span className="profile profile--pending" aria-hidden="true" />
      )}
    </header>
  );
}

function SignedInMark({ email, picture }: { email: string; picture: string | null }) {
  const [broken, setBroken] = useState(false);
  if (!picture || broken) return <LetterMark email={email} />;

  return (
    <a className="profile" href="/notices">
      <img
        alt={email}
        src={picture}
        width={30}
        height={30}
        referrerPolicy="no-referrer"
        onError={() => setBroken(true)}
      />
    </a>
  );
}

function LetterMark({ email }: { email: string }) {
  return (
    <a className="profile profile--letter" href="/notices" aria-label={email}>
      <span aria-hidden="true">{profileLetter(email)}</span>
    </a>
  );
}

function profileLetter(email: string): string {
  return email.trim().charAt(0).toLocaleUpperCase();
}
