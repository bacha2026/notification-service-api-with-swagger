import { HttpResponse, delay, http } from "msw";
import { setupServer } from "msw/node";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { CsrSnapshot } from "@/app/rendering/csr/csr-snapshot";
import type { RenderingSnapshot } from "@/app/rendering/_lib/rendering-data";

const endpoint = "http://localhost:3000/api/rendering-snapshot";

const snapshot: RenderingSnapshot = {
  mode: "CSR",
  fetchedAtUtc: "2026-08-17T01:00:00.000Z",
  renderedAtUtc: "2026-08-17T01:00:00.000Z",
  requestId: "csr-request",
  releaseId: "csr-release",
  executionEnvironment: "Next.js Route Handler",
  cachePolicy: "Browser fetch with cache: no-store from a no-store Route Handler",
  refreshBehavior: "Fetches after hydration and whenever Retry is selected",
  source: "Built-in rendering lab data; no network or external service",
};

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe("CsrSnapshot", () => {
  it("renders a loading shell before displaying the browser-fetched snapshot", async () => {
    server.use(
      http.get(endpoint, async () => {
        await delay(25);
        return HttpResponse.json(snapshot);
      }),
    );

    render(<CsrSnapshot />);

    expect(screen.getByTestId("csr-loading")).toBeInTheDocument();
    expect(await screen.findByTestId("snapshot-panel")).toBeInTheDocument();
    expect(screen.getByTestId("request-id")).toHaveTextContent("csr-request");
    expect(screen.getByText("Browser after React hydration")).toBeInTheDocument();
  });

  it("shows an error and can retry the request", async () => {
    let calls = 0;
    server.use(
      http.get(endpoint, () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ title: "Unavailable" }, { status: 503 })
          : HttpResponse.json(snapshot);
      }),
    );

    const user = userEvent.setup();
    render(<CsrSnapshot />);

    expect(await screen.findByRole("alert")).toHaveTextContent("failed (503)");
    await user.click(screen.getByRole("button", { name: "Retry" }));

    expect(await screen.findByTestId("snapshot-panel")).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it("aborts an in-flight request when the component unmounts", async () => {
    const captured: { signal?: AbortSignal } = {};
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation((_input, init) => {
      captured.signal = init?.signal instanceof AbortSignal ? init.signal : undefined;
      return new Promise((_resolve, reject) => {
        captured.signal?.addEventListener("abort", () => {
          reject(new DOMException("Aborted", "AbortError"));
        });
      });
    });

    const view = render(<CsrSnapshot />);
    expect(screen.getByTestId("csr-loading")).toBeInTheDocument();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce());
    view.unmount();

    expect(captured.signal?.aborted).toBe(true);
  });
});
