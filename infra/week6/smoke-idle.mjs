#!/usr/bin/env node

import { randomBytes, randomUUID } from "node:crypto";

const nextUrl = requiredEnvironment("NEXT_URL").replace(/\/+$/, "");
const apiUrl = requiredEnvironment("API_URL").replace(/\/+$/, "");
const expectedColor = requiredEnvironment("EXPECTED_COLOR");
const expectedSha = requiredEnvironment("EXPECTED_SHA");
const tokenSourceApiUrl = (process.env.TOKEN_SOURCE_API_URL || apiUrl).replace(/\/+$/, "");

function requiredEnvironment(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function fetchWithDeadline(url, init = {}, timeoutMilliseconds = 10_000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMilliseconds);
  try {
    return await fetch(url, { ...init, signal: controller.signal, cache: "no-store" });
  } finally {
    clearTimeout(timer);
  }
}

async function expectOk(url, description, init) {
  const response = await fetchWithDeadline(url, init);
  assert(response.ok, `${description} returned HTTP ${response.status}`);
  return response;
}

async function expectVersion(baseUrl, service) {
  const response = await expectOk(`${baseUrl}/health/version`, `${service} version endpoint`);
  const body = await response.json();
  assert(body.service === service, `${service} version endpoint reported '${body.service}'`);
  assert(body.color === expectedColor, `${service} reported color '${body.color}', expected '${expectedColor}'`);
  assert(body.sha === expectedSha, `${service} reported SHA '${body.sha}', expected '${expectedSha}'`);
  assert(response.headers.get("x-release-color") === expectedColor, `${service} release color header is incorrect`);
  assert(response.headers.get("x-release-sha") === expectedSha, `${service} release SHA header is incorrect`);
}

await expectVersion(nextUrl, "next");
await expectVersion(apiUrl, "notification-api");
await expectOk(`${apiUrl}/health/ready`, "API SQL readiness");

const renderingRoutes = ["/rendering/ssg", "/rendering/ssr", "/rendering/isr", "/rendering/csr"];
for (const route of renderingRoutes) {
  const response = await expectOk(`${nextUrl}${route}`, `${route} production render`);
  const html = await response.text();
  assert(html.includes(route.split("/").at(-1).toUpperCase()), `${route} did not render its mode label`);
}

const rootHtml = await (await expectOk(`${nextUrl}/`, "Next root page")).text();
const staticPath = rootHtml.match(/\/_next\/static\/[^"'<>\\\s]+/)?.[0];
assert(staticPath, "Next root HTML did not reference a versioned static asset");
await expectOk(`${nextUrl}${staticPath.replaceAll("&amp;", "&")}`, "versioned Next static asset");

// This traverses the Next BFF and proves it reaches the paired API color.
await expectOk(`${nextUrl}/api/notification/notifications`, "Next-to-API notification proxy");

const suffix = `${Date.now()}-${randomUUID().slice(0, 8)}`;
const email = `release-${suffix}@example.test`;
const password = `Nsa!${randomBytes(24).toString("base64url")}aA9`;
const probeClientIp = `198.51.100.${(randomBytes(1)[0] % 253) + 1}`;
const authenticationHeaders = {
  "content-type": "application/json",
  "x-forwarded-for": probeClientIp,
};
const signUpResponse = await fetchWithDeadline(`${tokenSourceApiUrl}/api/v2/auth/signup`, {
  method: "POST",
  headers: authenticationHeaders,
  body: JSON.stringify({
    email,
    password,
    firstName: "Release",
    lastName: "Probe",
  }),
});
assert(signUpResponse.status === 201, `token-source signup returned HTTP ${signUpResponse.status}`);
const signUp = await signUpResponse.json();
assert(typeof signUp.accessToken === "string" && signUp.accessToken.length > 20, "signup did not issue a token");

// If tokenSourceApiUrl is the active color, this is the blue-issued-token-on-
// green check. Both colors must use the same Vault-backed signing key.
await expectOk(`${apiUrl}/api/v2/auth/me`, "candidate validation of shared JWT", {
  headers: { authorization: `Bearer ${signUp.accessToken}` },
});

const loginResponse = await fetchWithDeadline(`${apiUrl}/api/v2/auth/login`, {
  method: "POST",
  headers: authenticationHeaders,
  body: JSON.stringify({ email, password }),
});
assert(loginResponse.status === 200, `candidate login returned HTTP ${loginResponse.status}`);

const idempotencyKey = `release-${randomUUID()}`;
const bulkBody = JSON.stringify({
  notifications: [{
    recipientEmail: email,
    channel: 1,
    subject: `Release proof ${expectedSha.slice(0, 12)}`,
    body: "Durable admission must succeed independently of immediate RabbitMQ delivery.",
    orderId: null,
  }],
});
const bulkUrl = `${nextUrl}/api/notification/notifications/bulk`;
const bulkHeaders = { "content-type": "application/json", "x-idempotency-key": idempotencyKey };
const acceptedResponse = await fetchWithDeadline(bulkUrl, { method: "POST", headers: bulkHeaders, body: bulkBody });
assert(acceptedResponse.status === 202, `bulk admission through Next returned HTTP ${acceptedResponse.status}`);
const accepted = await acceptedResponse.json();
assert(typeof accepted.jobId === "string", "bulk admission did not return a jobId");

const replayResponse = await fetchWithDeadline(bulkUrl, { method: "POST", headers: bulkHeaders, body: bulkBody });
assert(replayResponse.status === 202, `idempotent replay returned HTTP ${replayResponse.status}`);
assert(replayResponse.headers.get("idempotency-replayed") === "true", "Next BFF did not preserve Idempotency-Replayed");
const replay = await replayResponse.json();
assert(replay.jobId === accepted.jobId, "idempotent replay returned a different jobId");
await expectOk(`${nextUrl}/api/notification/notifications/bulk/${accepted.jobId}`, "durable bulk job status");

process.stdout.write(`${JSON.stringify({
  result: "pass",
  color: expectedColor,
  sha: expectedSha,
  checks: [
    "next-and-api-release-identity",
    "sql-readiness",
    "ssg-ssr-isr-csr",
    "versioned-static-asset",
    "next-api-proxy",
    "cross-color-jwt",
    "login",
    "idempotent-durable-admission",
  ],
})}\n`);
