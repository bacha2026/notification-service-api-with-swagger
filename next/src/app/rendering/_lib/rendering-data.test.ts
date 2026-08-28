import { describe, expect, it } from "vitest";
import { createRenderingSnapshot, renderingModes } from "@/app/rendering/_lib/rendering-data";

describe("createRenderingSnapshot", () => {
  it.each(renderingModes)("creates a complete %s snapshot", (mode) => {
    const snapshot = createRenderingSnapshot(mode, {
      fetchedAtUtc: "2026-08-17T01:00:00.000Z",
      renderedAtUtc: "2026-08-17T01:00:01.000Z",
      requestId: "request-123",
      releaseId: "release-456",
      executionEnvironment: "test runtime",
    });

    expect(snapshot).toMatchObject({
      mode,
      fetchedAtUtc: "2026-08-17T01:00:00.000Z",
      renderedAtUtc: "2026-08-17T01:00:01.000Z",
      requestId: "request-123",
      releaseId: "release-456",
      executionEnvironment: "test runtime",
    });
    expect(snapshot.cachePolicy).not.toHaveLength(0);
    expect(snapshot.refreshBehavior).not.toHaveLength(0);
    expect(snapshot.source).toContain("no network");
  });

  it("assigns a unique identifier when one is not supplied", () => {
    const first = createRenderingSnapshot("SSR");
    const second = createRenderingSnapshot("SSR");

    expect(first.requestId).not.toBe(second.requestId);
  });
});

