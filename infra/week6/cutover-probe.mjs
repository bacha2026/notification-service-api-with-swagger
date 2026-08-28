#!/usr/bin/env node

const publicUrl = required(2, "PUBLIC_URL").replace(/\/+$/, "");
const fromColor = process.argv[3] || "";
const fromSha = process.argv[4] || "";
const toColor = required(5, "TO_COLOR");
const toSha = required(6, "TO_SHA");
const durationMilliseconds = Number(process.argv[7] || 5000);

if (!Number.isInteger(durationMilliseconds) || durationMilliseconds < 1000 || durationMilliseconds > 120_000) {
  throw new Error("DURATION_MS must be an integer from 1000 through 120000");
}

function required(index, name) {
  const value = process.argv[index]?.trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
}

const allowed = new Set([`${toColor}\0${toSha}`]);
if (fromColor && fromSha) allowed.add(`${fromColor}\0${fromSha}`);
const observations = new Map();
const failures = [];
const startedAt = Date.now();
let requests = 0;

while (Date.now() - startedAt < durationMilliseconds) {
  requests += 1;
  try {
    const response = await fetch(`${publicUrl}/health/version`, {
      cache: "no-store",
      // Probe a fresh client connection on every request. Nginx deliberately
      // closes idle keep-alive sockets during a graceful worker replacement;
      // racing a pooled idle socket tests a client's retry policy rather than
      // whether the shared listener stayed continuously available.
      headers: {
        "cache-control": "no-cache",
        connection: "close",
        "x-correlation-id": `cutover-${requests}`,
      },
      signal: AbortSignal.timeout(2000),
    });
    const body = await response.json();
    const headerColor = response.headers.get("x-release-color");
    const headerSha = response.headers.get("x-release-sha");
    const key = `${body.color}\0${body.sha}`;
    if (!response.ok || body.service !== "next" || !allowed.has(key)
      || headerColor !== body.color || headerSha !== body.sha) {
      failures.push({ request: requests, status: response.status, body, headerColor, headerSha });
    } else {
      observations.set(key, (observations.get(key) || 0) + 1);
    }
  } catch (error) {
    failures.push({ request: requests, error: error.message });
  }
  await new Promise((resolve) => setTimeout(resolve, 50));
}

const targetKey = `${toColor}\0${toSha}`;
if (!observations.has(targetKey)) failures.push({ error: "target release was never observed" });
if (fromColor && fromSha && !observations.has(`${fromColor}\0${fromSha}`)) {
  failures.push({ error: "former release was not observed before the switch" });
}

const evidence = {
  result: failures.length === 0 ? "pass" : "fail",
  startedAtUtc: new Date(startedAt).toISOString(),
  durationMilliseconds: Date.now() - startedAt,
  requests,
  observations: [...observations].map(([key, count]) => {
    const [color, sha] = key.split("\0");
    return { color, sha, count };
  }),
  failures,
};
process.stdout.write(`${JSON.stringify(evidence, null, 2)}\n`);
if (failures.length) process.exitCode = 1;
