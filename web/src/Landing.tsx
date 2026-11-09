import { ConnectGmail } from "./auth/ConnectGmail";
import { Masthead, useMastheadSession } from "./Masthead";

export function Landing() {
  const session = useMastheadSession();
  const signedIn = session.kind === "signed-in";
  const signedOut = session.kind === "signed-out";

  return (
    <>
      <Masthead session={session} />
      <main>
        <section className="hero" aria-labelledby="hero-title">
          <div className="hero-copy">
            <h1 id="hero-title">Every Job Application, straight from your inbox.</h1>
            <p>
              AppTrack reads the confirmation emails companies send and opens a Job
              Application for each one, with the Company, the Role, and where it
              stands. You correct what the mail got wrong.
            </p>
            {window.location.search === "?connect=failed" && signedOut ? (
              <p className="connect-error" role="alert">
                Gmail couldn't be connected. Try again.
              </p>
            ) : null}
            {signedOut ? (
              <>
                <ConnectGmail />
                <p className="fineprint">
                  Read-only Gmail access. AppTrack never sends mail.
                </p>
              </>
            ) : signedIn ? (
              <a className="connect connect--primary" href="/notices">
                Notices
              </a>
            ) : null}
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
              <dt>Accepted</dt>
              <dd>The employer has made an offer.</dd>
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

function ApplicationSpecimen() {
  return (
    <aside className="specimen" aria-label="Example Job Application">
      <p className="specimen-label">Example</p>
      <p className="specimen-company">Northwind Freight</p>
      <p className="specimen-role">Data Analyst</p>
      <ol className="specimen-path">
        <li>Application Sent</li>
        <li>Interviewing</li>
        <li>Accepted</li>
      </ol>
      <p className="specimen-source">From “Your application to Northwind Freight”</p>
    </aside>
  );
}
