import { NextRequest, NextResponse } from "next/server";

const notificationApiBaseUrls = process.env.NOTIFICATION_API_URL
  ? [process.env.NOTIFICATION_API_URL.replace(/\/+$/, "")]
  : ["http://localhost:8080", "http://localhost:5280"];

type RouteParams = {
  params: Promise<{ path: string[] }>;
};

async function proxyNotificationApi(request: NextRequest, { params }: RouteParams) {
  const { path } = await params;
  const encodedPath = path.map((segment) => encodeURIComponent(segment)).join("/");
  const contentType = request.headers.get("content-type");
  const accept = request.headers.get("accept");
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer();
  const headers = new Headers();

  if (contentType) headers.set("content-type", contentType);
  if (accept) headers.set("accept", accept);

  try {
    let upstreamResponse: Response | undefined;

    for (const baseUrl of notificationApiBaseUrls) {
      try {
        upstreamResponse = await fetch(`${baseUrl}/api/v2/${encodedPath}${request.nextUrl.search}`, {
          method: request.method,
          headers,
          body,
          cache: "no-store",
        });
        break;
      } catch {
        // Support both the local dotnet launch profile and the compose runtime.
      }
    }

    if (!upstreamResponse) throw new Error("Notification API is unavailable.");

    const responseHeaders = new Headers();
    const upstreamContentType = upstreamResponse.headers.get("content-type");

    if (upstreamContentType) responseHeaders.set("content-type", upstreamContentType);

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
