import { useEffect, useId, useRef, useState } from "react";
import { ConnectGmail } from "./auth/ConnectGmail";
import {
  forgetProfile,
  isAbortError,
  loadLandingSession,
  readRememberedProfile,
  type SessionProfile,
} from "./auth/loadSessionProfile";

export type MastheadSession = { kind: "pending" } | SessionProfile;

const SIGN_OUT_API = "/api/session/sign-out";

export function useMastheadSession(): {
  session: MastheadSession;
  signOut: () => Promise<boolean>;
} {
  const [state, setState] = useState<MastheadSession>(() => {
    const remembered = readRememberedProfile();
    return remembered === null ? { kind: "pending" } : { kind: "signed-in", ...remembered };
  });
  const loadRef = useRef<AbortController | null>(null);
  const leftRef = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    loadRef.current = controller;
    let cancelled = false;

    loadLandingSession(controller.signal).then(
      (result) => {
        if (cancelled || leftRef.current) return;
        setState(result);
      },
      (error: unknown) => {
        if (cancelled || leftRef.current || isAbortError(error)) return;
        forgetProfile();
        setState({ kind: "signed-out" });
      },
    );

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, []);

  async function signOut(): Promise<boolean> {
    let response: Response;
    try {
      response = await fetch(SIGN_OUT_API, { method: "POST" });
    } catch {
      return false;
    }
    if (!response.ok) return false;
    leftRef.current = true;
    loadRef.current?.abort();
    forgetProfile();
    setState({ kind: "signed-out" });
    return true;
  }

  return { session: state, signOut };
}

export function Masthead({
  session,
  signedOut = false,
  onSignOut,
}: {
  session: MastheadSession;
  signedOut?: boolean;
  onSignOut: () => Promise<boolean>;
}) {
  const showOut = signedOut || session.kind === "signed-out";
  const showIn = !showOut && session.kind === "signed-in";

  return (
    <header className="masthead">
      <a className="wordmark" href="/">
        AppTrack
      </a>
      {showIn ? (
        <SignedInMark email={session.email} picture={session.picture} onSignOut={onSignOut} />
      ) : showOut ? (
        <ConnectGmail emphasis="quiet" />
      ) : (
        <span className="profile profile--pending" aria-hidden="true" />
      )}
    </header>
  );
}

function SignedInMark({
  email,
  picture,
  onSignOut,
}: {
  email: string;
  picture: string | null;
  onSignOut: () => Promise<boolean>;
}) {
  const [broken, setBroken] = useState(false);
  const [open, setOpen] = useState(false);
  const [failed, setFailed] = useState(false);
  const menuId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemRef = useRef<HTMLButtonElement>(null);
  const focusItem = useRef(false);
  const letter = !picture || broken;

  useEffect(() => {
    if (open && focusItem.current) {
      focusItem.current = false;
      itemRef.current?.focus();
    }
  }, [open]);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      setOpen(false);
      triggerRef.current?.focus();
    }

    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  async function signOut() {
    setFailed(false);
    const ok = await onSignOut();
    if (ok) setOpen(false);
    else setFailed(true);
  }

  return (
    <div className="profile-menu" ref={rootRef}>
      <button
        type="button"
        className={letter ? "profile profile--letter" : "profile"}
        aria-label={letter ? email : undefined}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        ref={triggerRef}
        onClick={(event) => {
          setFailed(false);
          const next = !open;
          focusItem.current = next && event.detail === 0;
          setOpen(next);
        }}
      >
        {letter ? (
          <span aria-hidden="true">{profileLetter(email)}</span>
        ) : (
          <img
            alt={email}
            src={picture ?? undefined}
            width={30}
            height={30}
            referrerPolicy="no-referrer"
            onError={() => setBroken(true)}
          />
        )}
      </button>
      {open ? (
        <div className="profile-menu-panel" id={menuId} role="menu">
          <button type="button" role="menuitem" ref={itemRef} onClick={() => void signOut()}>
            Sign out
          </button>
          {failed ? <p role="alert">Couldn't sign out. Try again.</p> : null}
        </div>
      ) : null}
    </div>
  );
}

function profileLetter(email: string): string {
  return email.trim().charAt(0).toLocaleUpperCase();
}
