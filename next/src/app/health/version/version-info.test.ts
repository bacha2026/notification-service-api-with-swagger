import { afterEach, describe, expect, it, vi } from "vitest";
import { GET } from "@/app/health/version/route";
import { getVersionInfo } from "@/app/health/version/version-info";

afterEach(() => vi.unstubAllEnvs());

describe("getVersionInfo", () => {
  it("reports the runtime release color and SHA", () => {
    expect(
      getVersionInfo({
        RELEASE_COLOR: "green",
        RELEASE_ID: "a1b2c3d4",
      }),
    ).toEqual({
      service: "next",
      color: "green",
      sha: "a1b2c3d4",
    });
  });

  it("uses safe local labels for absent or header-unsafe values", () => {
    expect(
      getVersionInfo({
        RELEASE_COLOR: " ",
        RELEASE_ID: "bad/value",
      }),
    ).toEqual({
      service: "next",
      color: "local",
      sha: "development",
    });
  });
});

describe("GET /health/version", () => {
  it("returns non-cacheable release JSON and matching headers", async () => {
    vi.stubEnv("RELEASE_COLOR", "blue");
    vi.stubEnv("RELEASE_ID", "release-123");

    const response = GET();

    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toContain("no-store");
    expect(response.headers.get("x-release-color")).toBe("blue");
    expect(response.headers.get("x-release-sha")).toBe("release-123");
    await expect(response.json()).resolves.toEqual({
      service: "next",
      color: "blue",
      sha: "release-123",
    });
  });
});
