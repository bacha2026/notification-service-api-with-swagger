import { NextRequest } from "next/server";
import { describe, expect, it, vi } from "vitest";
import { POST } from "@/app/api/notification/[...path]/route";

describe("notification API proxy", () => {
  it("preserves idempotency, tracing, forwarding, replay, and release headers", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ id: 42 }), {
        status: 201,
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Replayed": "true",
          "X-Correlation-ID": "correlation-456",
          "X-Release-Color": "green",
          "X-Release-Sha": "sha-789",
        },
      }),
    );
    const request = new NextRequest("https://store.example/api/notification/auth/orders", {
      method: "POST",
      headers: {
        Accept: "application/json",
        Authorization: "Bearer test-token",
        Baggage: "tenant=test",
        "Content-Type": "application/json",
        Cookie: "must-not-reach-upstream=true",
        Traceparent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        Tracestate: "vendor=value",
        "X-Correlation-ID": "correlation-123",
        "X-Forwarded-For": "203.0.113.10",
        "X-Forwarded-Host": "public.example",
        "X-Forwarded-Proto": "https",
        "X-Idempotency-Key": "checkout-123",
      },
      body: JSON.stringify({}),
    });

    const response = await POST(request, {
      params: Promise.resolve({ path: ["auth", "orders"] }),
    });

    expect(fetchMock).toHaveBeenCalledOnce();
    const [upstreamUrl, init] = fetchMock.mock.calls[0];
    const headers = new Headers(init?.headers);

    expect(upstreamUrl).toBe("http://localhost:8081/api/v2/auth/orders");
    expect(init?.method).toBe("POST");
    expect(init?.cache).toBe("no-store");
    expect(init?.signal).toBe(request.signal);
    expect(headers.get("authorization")).toBe("Bearer test-token");
    expect(headers.get("x-idempotency-key")).toBe("checkout-123");
    expect(headers.get("x-correlation-id")).toBe("correlation-123");
    expect(headers.get("traceparent")).toContain("4bf92f3577b34da6a3ce929d0e0e4736");
    expect(headers.get("tracestate")).toBe("vendor=value");
    expect(headers.get("baggage")).toBe("tenant=test");
    expect(headers.get("x-forwarded-for")).toBe("203.0.113.10");
    expect(headers.get("x-forwarded-host")).toBe("public.example");
    expect(headers.get("x-forwarded-proto")).toBe("https");
    expect(headers.has("cookie")).toBe(false);

    expect(response.status).toBe(201);
    expect(response.headers.get("idempotency-replayed")).toBe("true");
    expect(response.headers.get("x-correlation-id")).toBe("correlation-456");
    expect(response.headers.get("x-release-color")).toBe("green");
    expect(response.headers.get("x-release-sha")).toBe("sha-789");
    await expect(response.json()).resolves.toEqual({ id: 42 });
  });

  it("derives forwarded host and protocol for direct requests", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(null, { status: 204 }),
    );
    const request = new NextRequest("http://localhost:3000/api/notification/products", {
      method: "POST",
    });

    await POST(request, {
      params: Promise.resolve({ path: ["products"] }),
    });

    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get("x-forwarded-host")).toBe("localhost:3000");
    expect(headers.get("x-forwarded-proto")).toBe("http");
  });
});
