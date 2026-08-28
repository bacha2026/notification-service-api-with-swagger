import { expect, test, type Page } from "@playwright/test";

type VisibleSnapshot = {
  fetchedAtUtc: string;
  renderedAtUtc: string;
  requestId: string;
  releaseId: string;
};

async function readSnapshot(page: Page): Promise<VisibleSnapshot> {
  await expect(page.getByTestId("snapshot-panel")).toBeVisible();

  return {
    fetchedAtUtc: (await page.getByTestId("fetch-timestamp").textContent())?.trim() ?? "",
    renderedAtUtc: (await page.getByTestId("render-timestamp").textContent())?.trim() ?? "",
    requestId: (await page.getByTestId("request-id").textContent())?.trim() ?? "",
    releaseId: (await page.getByTestId("release-id").textContent())?.trim() ?? "",
  };
}

test.describe("production rendering semantics", () => {
  test("release health reports the runtime identity without caching", async ({ request }) => {
    const response = await request.get("/health/version");
    const version = (await response.json()) as {
      service: string;
      color: string;
      sha: string;
    };

    expect(response.ok()).toBe(true);
    expect(response.headers()["cache-control"]).toContain("no-store");
    expect(version.service).toBe("next");
    expect(version.color).not.toHaveLength(0);
    expect(version.sha).not.toHaveLength(0);
    expect(response.headers()["x-release-color"]).toBe(version.color);
    expect(response.headers()["x-release-sha"]).toBe(version.sha);
  });

  test("SSG remains fixed for the lifetime of a build", async ({ page }) => {
    await page.goto("/rendering/ssg");
    const first = await readSnapshot(page);
    await page.waitForTimeout(50);
    await page.reload();
    const second = await readSnapshot(page);

    expect(second).toEqual(first);
  });

  test("SSR creates a new snapshot for every request", async ({ page }) => {
    await page.goto("/rendering/ssr");
    const first = await readSnapshot(page);
    await page.waitForTimeout(50);
    await page.reload();
    const second = await readSnapshot(page);

    expect(second.releaseId).toBe(first.releaseId);
    expect(second.requestId).not.toBe(first.requestId);
    expect(second.fetchedAtUtc).not.toBe(first.fetchedAtUtc);
    expect(second.renderedAtUtc).not.toBe(first.renderedAtUtc);
  });

  test("ISR stays fixed inside its window and refreshes after expiry", async ({ page }) => {
    await page.goto("/rendering/isr");
    let baseline = await readSnapshot(page);

    // A build may be older than the revalidation window when this test starts.
    // Settle any background regeneration before measuring the stable window.
    for (let attempt = 0; attempt < 10; attempt += 1) {
      await page.waitForTimeout(250);
      await page.reload();
      const candidate = await readSnapshot(page);
      if (candidate.requestId === baseline.requestId) break;
      baseline = candidate;
    }

    await page.reload();
    const withinWindow = await readSnapshot(page);
    expect(withinWindow).toEqual(baseline);

    await page.waitForTimeout(5_250);
    await page.reload();

    let refreshed = await readSnapshot(page);
    for (let attempt = 0; attempt < 20 && refreshed.requestId === baseline.requestId; attempt += 1) {
      await page.waitForTimeout(250);
      await page.reload();
      refreshed = await readSnapshot(page);
    }

    expect(refreshed.releaseId).toBe(baseline.releaseId);
    expect(refreshed.requestId).not.toBe(baseline.requestId);
    expect(refreshed.fetchedAtUtc).not.toBe(baseline.fetchedAtUtc);
  });

  test("CSR fetches after hydration and the endpoint never caches", async ({ page, request }) => {
    await page.route("**/api/rendering-snapshot", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 300));
      await route.continue();
    });

    await page.goto("/rendering/csr");
    await expect(page.getByTestId("csr-loading")).toBeVisible();
    const browserSnapshot = await readSnapshot(page);

    expect(browserSnapshot.requestId).not.toHaveLength(0);
    expect(Date.parse(browserSnapshot.renderedAtUtc)).toBeGreaterThanOrEqual(
      Date.parse(browserSnapshot.fetchedAtUtc),
    );

    const firstResponse = await request.get("/api/rendering-snapshot");
    const secondResponse = await request.get("/api/rendering-snapshot");
    const first = (await firstResponse.json()) as VisibleSnapshot;
    const second = (await secondResponse.json()) as VisibleSnapshot;

    expect(firstResponse.headers()["cache-control"]).toContain("no-store");
    expect(second.requestId).not.toBe(first.requestId);
  });
});
