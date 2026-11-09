export async function onRequest(context) {
  const base = context.env.API_ORIGIN;
  if (!base) {
    return new Response("API_ORIGIN is not configured.", { status: 500 });
  }

  const incoming = new URL(context.request.url);
  const origin = new URL(incoming.pathname + incoming.search, base);
  return fetch(origin, {
    method: context.request.method,
    headers: context.request.headers,
    body: context.request.body,
    redirect: "manual",
  });
}
