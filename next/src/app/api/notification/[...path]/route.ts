import { NextRequest, NextResponse } from "next/server";

const notificationApiBaseUrls = process.env.NOTIFICATION_API_URL
  ? [process.env.NOTIFICATION_API_URL.replace(/\/+$/, "")]
  : ["http://localhost:8081", "http://localhost:5280"];

type RouteParams = {
  params: Promise<{ path: string[] }>;
};

async function proxyNotificationApi(request: NextRequest, { params }: RouteParams) {
  const { path } = await params;
  const encodedPath = path.map((segment) => encodeURIComponent(segment)).join("/");
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer();
  const headers = new Headers();
  const forwardedRequestHeaders = [
    "accept",
    "authorization",
    "baggage",
    "content-type",
    "traceparent",
    "tracestate",
    "x-correlation-id",
    "x-forwarded-for",
    "x-idempotency-key",
  ];

  for (const name of forwardedRequestHeaders) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }

  // Preserve edge-supplied client metadata when present; otherwise derive
  // host/protocol from the URL parsed by Next.js.
  headers.set(
    "x-forwarded-host",
    request.headers.get("x-forwarded-host") ?? request.headers.get("host") ?? request.nextUrl.host,
  );
  headers.set(
    "x-forwarded-proto",
    request.headers.get("x-forwarded-proto") ?? request.nextUrl.protocol.replace(/:$/, ""),
  );
  // JWTs are supplied from the in-memory client store and must reach the API.
  // Do not forward browser cookies here: this proxy uses explicit bearer auth.

  try {
    let upstreamResponse: Response | undefined;

    for (const baseUrl of notificationApiBaseUrls) {
      try {
        upstreamResponse = await fetch(`${baseUrl}/api/v2/${encodedPath}${request.nextUrl.search}`, {
          method: request.method,
          headers,
          body,
          cache: "no-store",
          signal: request.signal,
        });
        break;
      } catch {
        // Support both the local dotnet launch profile and the compose runtime.
      }
    }

    if (!upstreamResponse) throw new Error("Notification API is unavailable.");

    const responseHeaders = new Headers();
    const forwardedResponseHeaders = [
      "content-type",
      "www-authenticate",
      "location",
      "link",
      "idempotency-replayed",
      "x-correlation-id",
      "x-notification-handoff",
      "x-notification-job-id",
      "x-release-color",
      "x-release-sha",
    ];

    for (const name of forwardedResponseHeaders) {
      const value = upstreamResponse.headers.get(name);
      if (value) responseHeaders.set(name, value);
    }

    if (upstreamResponse.status === 204) {
      return new NextResponse(null, { status: upstreamResponse.status, headers: responseHeaders });
    }

    return new NextResponse(await upstreamResponse.arrayBuffer(), {
      status: upstreamResponse.status,
      headers: responseHeaders,
    });
  } catch {
    return NextResponse.json(
      {
        title: "Notification API is unavailable.",
        detail: "Start notification-api or set NOTIFICATION_API_URL to its base URL.",
      },
      { status: 503 },
    );
  }
}

export const GET = proxyNotificationApi;
export const POST = proxyNotificationApi;
export const PUT = proxyNotificationApi;
export const PATCH = proxyNotificationApi;
export const DELETE = proxyNotificationApi;
