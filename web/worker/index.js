export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/api" || url.pathname.startsWith("/api/")) {
      if (!env.API_ORIGIN) {
        return new Response("API_ORIGIN is not configured.", { status: 500 });
      }

      const origin = new URL(url.pathname + url.search, env.API_ORIGIN);
      return fetch(origin, {
        method: request.method,
        headers: request.headers,
        body: request.body,
        redirect: "manual",
      });
    }

    return env.ASSETS.fetch(request);
  },
};
