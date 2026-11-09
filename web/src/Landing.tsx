import { useEffect, useState } from "react";
import { ConnectGmail } from "./auth/ConnectGmail";
import { isAbortError, loadSessionProfile, type SessionProfile } from "./auth/loadSessionProfile";

type LandingSession = { kind: "pending" } | SessionProfile;

export function useLandingSession(): LandingSession {
  const [state, setState] = useState<LandingSession>({ kind: "pending" });

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;

    loadSessionProfile(controller.signal).then(
      (result) => {
        if (!cancelled) setState(result);
      },
      (error: unknown) => {
        if (cancelled || isAbortError(error)) return;
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

export function Landing() {
  const session = useLandingSession();
  const signedIn = session.kind === "signed-in";

  return (
    <>
      <header className="masthead">
        <a className="wordmark" href="/">
          AppTrack
        </a>
        {signedIn ? (
          <SignedInMark email={session.email} picture={session.picture} />
        ) : (
          <ConnectGmail emphasis="quiet" />
        )}
      </header>
      <main>
        <section className="hero" aria-labelledby="hero-title">
          <div className="hero-copy">
            <h1 id="hero-title">Every Job Application, straight from your inbox.</h1>
            <p>
              AppTrack reads the confirmation emails companies send and opens a Job
              Application for each one, with the Company, the Role, and where it
              stands. You correct what the mail got wrong.
            </p>
            {window.location.search === "?connect=failed" && !signedIn ? (
              <p className="connect-error" role="alert">
                Gmail couldn't be connected. Try again.
              </p>
            ) : null}
            {signedIn ? null : (
              <>
                <ConnectGmail />
                <p className="fineprint">
                  Read-only Gmail access. AppTrack never sends mail.
                </p>
              </>
            )}
          </div>
          <ApplicationSpecimen />
        </section>
        <section className="statuses" aria-labelledby="status-title">
          <h2 id="status-title">Where a candidacy stands</h2>
          <dl>
            <div>
              <dt>Application Sent</dt>
              <dd>The candidacy has been submitted.</dd>
            </div>
            <div>
              <dt>Interviewing</dt>
              <dd>
                The candidacy has moved forward to a screen, an interview, or a task.
              </dd>
            </div>
            <div>
              <dt>Rejected</dt>
              <dd>The employer has ended the candidacy.</dd>
            </div>
          </dl>
        </section>
      </main>
      <footer className="colophon">
        <p>One candidacy. One Role. One Company.</p>
      </footer>
    </>
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
        width={40}
        height={40}
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

function ApplicationSpecimen() {
  return (
    <aside className="specimen" aria-label="Example Job Application">
      <p className="specimen-label">Example</p>
      <p className="specimen-company">Northwind Freight</p>
      <p className="specimen-role">Data Analyst</p>
      <ol className="specimen-path">
        <li>Application Sent</li>
        <li>Interviewing</li>
      </ol>
      <p className="specimen-source">From “Your application to Northwind Freight”</p>
    </aside>
  );
}
