import "@fontsource-variable/source-serif-4";
import "@fontsource/ibm-plex-sans/400.css";
import "@fontsource/ibm-plex-sans/500.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Landing } from "./Landing";
import { NoticesPage } from "./notices/NoticesPage";
import "./styles.css";

const root = document.getElementById("root");
if (!(root instanceof HTMLElement)) {
  throw new Error("missing #root");
}

createRoot(root).render(
  <StrictMode>
    {window.location.pathname === "/notices" ? <NoticesPage /> : <Landing />}
  </StrictMode>,
);
