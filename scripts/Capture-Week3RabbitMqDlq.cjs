const path = require("node:path");
const { chromium } = require("playwright-core");

const [managementBaseUrl, username, password, outputPath] = process.argv.slice(2);
if (!managementBaseUrl || !username || !password || !outputPath) {
  throw new Error("Expected management URL, username, password, and output path.");
}

const edgePath = process.env.WEEK3_EDGE_PATH;
if (!edgePath) {
  throw new Error("WEEK3_EDGE_PATH must point to the Microsoft Edge executable.");
}

(async () => {
  const browser = await chromium.launch({ executablePath: edgePath, headless: true });
  try {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 1000 },
      colorScheme: "light",
    });
    const page = await context.newPage();

    await page.goto(managementBaseUrl, { waitUntil: "domcontentloaded" });
    await page.locator("#username").fill(username);
    await page.locator("#password").fill(password);
    await page.locator('input[type="submit"]').click();
    await page.waitForFunction(() => Boolean(document.querySelector("#main")), null, {
      timeout: 20_000,
    });

    await page.evaluate(() => {
      window.location.hash = "#/queues/%2F/nsa.notifications.bulk.dlq";
    });
    await page.waitForFunction(
      () => document.body.innerText.includes("nsa.notifications.bulk.dlq"),
      null,
      { timeout: 20_000 },
    );
    await page.waitForTimeout(3_000);

    const bodyText = await page.locator("body").innerText();
    if (!bodyText.includes("Ready") || !bodyText.includes("quorum")) {
      throw new Error("The queue page did not render its message and queue-type details.");
    }

    await page.screenshot({ path: path.resolve(outputPath), fullPage: true });
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
