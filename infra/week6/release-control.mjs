#!/usr/bin/env node

import { createHash } from "node:crypto";
import { chmodSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { isDeepStrictEqual } from "node:util";

const requiredComponents = ["api", "next", "migrator"];
const optionalComponents = ["worker", "outbox-worker", "dlq-worker"];
const supportedComponents = new Set([...requiredComponents, ...optionalComponents]);
const publishedImageNames = {
  api: "notification-api",
  next: "notification-web",
  migrator: "notification-migrator",
  worker: "notification-worker",
  "outbox-worker": "notification-outbox-worker",
  "dlq-worker": "notification-dlq-worker",
};
const trustedPublishWorkflowPath = ".github/workflows/publish.yml";
const releaseLabelPattern = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
const commitShaPattern = /^[a-f0-9]{40}$/;
const repositoryPattern = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/;
const runIdPattern = /^[1-9][0-9]*$/;
const localImageIdPattern = /^sha256:[a-f0-9]{64}$/;
const digestReferencePattern = /^[A-Za-z0-9][A-Za-z0-9._:/-]*@sha256:[a-f0-9]{64}$/;
const digestPattern = /^sha256:[a-f0-9]{64}$/;

function fail(message) {
  process.stderr.write(`release-control: ${message}\n`);
  process.exit(1);
}

function readJson(path, description) {
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    fail(`cannot read ${description} '${path}': ${error.message}`);
  }
}

function sha256File(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function validateCompatibility(value) {
  if (!value || value.databaseStrategy !== "expand-migrate-contract") {
    fail("manifest must declare compatibility.databaseStrategy=expand-migrate-contract");
  }
  if (value.rollbackSupported !== true) {
    fail("manifest must explicitly declare compatibility.rollbackSupported=true");
  }
  if (value.jwtKeyScope !== "shared-vault") {
    fail("manifest must declare compatibility.jwtKeyScope=shared-vault");
  }
  if (!Array.isArray(value.messageEnvelopeVersions) || !value.messageEnvelopeVersions.includes(1)) {
    fail("manifest must accept message envelope version 1");
  }
}

function validateReference(reference, component) {
  if (typeof reference !== "string"
    || (!localImageIdPattern.test(reference) && !digestReferencePattern.test(reference))) {
    fail(`${component} must use an exact registry digest or local content-addressed image ID`);
  }
}

function loadManifest(path) {
  const absolutePath = resolve(path);
  const document = readJson(absolutePath, "release manifest");
  if (document.schemaVersion !== 1) fail("unsupported release manifest schemaVersion");
  if (typeof document.revision !== "string" || !releaseLabelPattern.test(document.revision)) {
    fail("manifest revision is not a safe runtime release label");
  }
  if (!Array.isArray(document.images)) fail("manifest images must be an array");
  validateCompatibility(document.compatibility);

  const images = {};
  for (const entry of document.images) {
    if (!entry || typeof entry.component !== "string" || typeof entry.reference !== "string") {
      fail("every manifest image requires component and reference strings");
    }
    if (!supportedComponents.has(entry.component)) {
      fail(`manifest contains unsupported component '${entry.component}'`);
    }
    if (Object.hasOwn(images, entry.component)) fail(`duplicate manifest component '${entry.component}'`);
    validateReference(entry.reference, entry.component);
    images[entry.component] = entry.reference;
  }
  for (const component of requiredComponents) {
    if (!images[component]) fail(`manifest is missing required component '${component}'`);
  }

  return {
    document,
    revision: document.revision,
    images,
    compatibility: document.compatibility,
    manifestSha256: sha256File(absolutePath),
    manifestPath: absolutePath,
  };
}

function validateTrustedInputs(repository, runId, revision) {
  if (typeof repository !== "string" || !repositoryPattern.test(repository)) {
    fail("expected repository must use OWNER/REPOSITORY syntax");
  }
  if (typeof runId !== "string" || !runIdPattern.test(runId)) {
    fail("publish run ID must be a positive decimal integer");
  }
  if (typeof revision !== "string" || !commitShaPattern.test(revision)) {
    fail("published revision must be an exact lowercase 40-character commit SHA");
  }
}

function verifyPublishRun(path, expectedRepository, expectedRunId, expectedRevision) {
  validateTrustedInputs(expectedRepository, expectedRunId, expectedRevision);
  const run = readJson(resolve(path), "publish workflow run metadata");
  if (String(run.id) !== expectedRunId) fail("selected workflow run ID does not match the request");
  if (run.repository?.full_name !== expectedRepository
    || run.head_repository?.full_name !== expectedRepository) {
    fail("selected workflow run was not sourced from the expected repository");
  }
  if (run.path !== trustedPublishWorkflowPath) {
    fail(`selected workflow run did not execute ${trustedPublishWorkflowPath}`);
  }
  if (run.event !== "push") fail("trusted publication must be triggered by a push");
  if (run.status !== "completed" || run.conclusion !== "success") {
    fail("selected publish workflow run did not complete successfully");
  }
  if (run.head_sha !== expectedRevision) {
    fail("selected publish workflow revision does not match the requested commit");
  }
}

function validatePublishedWorkflowRun(value, expectedSource, expectedRunId) {
  const expectedUrl = `${expectedSource}/actions/runs/${expectedRunId}`;
  if (!value || String(value.id) !== expectedRunId || value.url !== expectedUrl) {
    fail("manifest workflow-run metadata does not match the selected trusted run");
  }
  if (!runIdPattern.test(String(value.attempt))) {
    fail("manifest workflow-run attempt must be a positive decimal integer");
  }
}

function verifyPublishedManifest(path, expectedSource, expectedRepository, expectedRunId, expectedRevision) {
  validateTrustedInputs(expectedRepository, expectedRunId, expectedRevision);
  const expectedSourceUrl = `https://github.com/${expectedRepository}`;
  if (expectedSource !== expectedSourceUrl) {
    fail(`expected source must be ${expectedSourceUrl}`);
  }

  const manifest = loadManifest(path);
  const { document } = manifest;
  if (manifest.revision !== expectedRevision) fail("manifest revision does not match the requested commit");
  if (document.source !== expectedSource) fail("manifest source does not match the expected repository");
  if (document.gitRef !== "refs/heads/main"
    && !(typeof document.gitRef === "string" && /^refs\/tags\/v[^\s]+$/.test(document.gitRef))) {
    fail("published manifest gitRef must be main or a v-prefixed release tag");
  }
  validatePublishedWorkflowRun(document.workflowRun, expectedSource, expectedRunId);

  const expectedComponents = Object.keys(publishedImageNames);
  if (document.images.length !== expectedComponents.length
    || !expectedComponents.every((component) => Object.hasOwn(manifest.images, component))) {
    fail("published manifest must contain exactly the six release components");
  }

  const owner = expectedRepository.split("/", 1)[0].toLowerCase();
  for (const entry of document.images) {
    const imageName = publishedImageNames[entry.component];
    if (!imageName) fail(`published manifest contains unexpected component '${entry.component}'`);
    const expectedImage = `ghcr.io/${owner}/${imageName}`;
    if (entry.image !== expectedImage) fail(`${entry.component} image is outside the trusted GHCR namespace`);
    if (typeof entry.digest !== "string" || !digestPattern.test(entry.digest)) {
      fail(`${entry.component} digest is not an immutable SHA-256 digest`);
    }
    if (entry.reference !== `${entry.image}@${entry.digest}`) {
      fail(`${entry.component} reference does not match its image and digest`);
    }
    if (entry.revision !== expectedRevision || entry.gitRef !== document.gitRef
      || entry.source !== expectedSource) {
      fail(`${entry.component} provenance metadata does not match the release`);
    }
    validatePublishedWorkflowRun(entry.workflowRun, expectedSource, expectedRunId);
    if (!Array.isArray(entry.tags) || !entry.tags.includes(`${entry.image}:sha-${expectedRevision}`)) {
      fail(`${entry.component} is missing its immutable commit-SHA tag record`);
    }
    if (entry.attestations?.sbom !== true
      || entry.attestations?.buildkitProvenance !== true
      || entry.attestations?.signedGitHubProvenance !== true) {
      fail(`${entry.component} is missing required attestation declarations`);
    }
  }
}

function loadState(path, optional = false) {
  let document;
  try {
    document = JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    if (optional && error.code === "ENOENT") return null;
    fail(`cannot read deployment state '${path}': ${error.message}`);
  }
  if (document.schemaVersion !== 1 || !["blue", "green"].includes(document.activeColor)) {
    fail(`deployment state '${path}' is invalid`);
  }
  validateReleaseRecord(document.active, "active deployment state");
  if (document.previous !== null && document.previous !== undefined) {
    validateReleaseRecord(document.previous, "previous deployment state");
  }
  return document;
}

function validateReleaseRecord(value, description) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    fail(`${description} must be a release object`);
  }
  if (typeof value.revision !== "string" || !releaseLabelPattern.test(value.revision)) {
    fail(`${description} has an invalid revision`);
  }
  if (typeof value.manifestSha256 !== "string" || !/^[a-f0-9]{64}$/.test(value.manifestSha256)) {
    fail(`${description} has an invalid manifest SHA-256`);
  }
  if (!value.images || typeof value.images !== "object" || Array.isArray(value.images)) {
    fail(`${description} images must be an object`);
  }

  const components = Object.keys(value.images);
  if (components.some((component) => !supportedComponents.has(component))) {
    fail(`${description} contains an unsupported image component`);
  }
  for (const component of requiredComponents) {
    if (!Object.hasOwn(value.images, component)) {
      fail(`${description} is missing required image '${component}'`);
    }
  }
  for (const component of components) {
    validateReference(value.images[component], `${description} ${component}`);
  }
  validateCompatibility(value.compatibility);
}

function writeAtomic(path, content, mode = 0o600) {
  const absolutePath = resolve(path);
  mkdirSync(dirname(absolutePath), { recursive: true, mode: 0o700 });
  const temporaryPath = `${absolutePath}.${process.pid}.tmp`;
  writeFileSync(temporaryPath, content, { encoding: "utf8", mode });
  chmodSync(temporaryPath, mode);
  renameSync(temporaryPath, absolutePath);
}

function releaseFromManifest(manifest) {
  return {
    revision: manifest.revision,
    manifestSha256: manifest.manifestSha256,
    images: manifest.images,
    compatibility: manifest.compatibility,
  };
}

function envLine(name, value) {
  if (typeof value !== "string" || /[\r\n\0]/.test(value)) fail(`unsafe value for ${name}`);
  return `${name}=${value}`;
}

function writeComposeEnvironment(path, blue, green, migratorImage) {
  const lines = [
    envLine("BLUE_API_IMAGE", blue.images.api),
    envLine("BLUE_NEXT_IMAGE", blue.images.next),
    envLine("BLUE_RELEASE_SHA", blue.revision),
    envLine("GREEN_API_IMAGE", green.images.api),
    envLine("GREEN_NEXT_IMAGE", green.images.next),
    envLine("GREEN_RELEASE_SHA", green.revision),
    envLine("MIGRATOR_IMAGE", migratorImage),
    "",
  ];
  writeAtomic(path, lines.join("\n"));
}

function requirePrevious(state, manifest) {
  if (!state?.previous) fail("no verified previous release is recorded; rollback is unavailable");
  if (state.previous.manifestSha256 !== manifest.manifestSha256
    || state.previous.revision !== manifest.revision) {
    fail("supplied rollback manifest is not the exact previous manifest recorded at cutover");
  }
  const expectedPrevious = releaseFromManifest(manifest);
  if (!isDeepStrictEqual(state.previous, expectedPrevious)) {
    fail("recorded previous release does not exactly match its archived manifest");
  }
}

function appendHistory(state, event) {
  const history = Array.isArray(state?.history) ? state.history.slice(-99) : [];
  history.push({ atUtc: new Date().toISOString(), ...event });
  return history;
}

function writeState(path, document) {
  writeAtomic(path, `${JSON.stringify(document, null, 2)}\n`);
}

function inspectManifest(path) {
  const manifest = loadManifest(path);
  process.stdout.write([
    manifest.revision,
    ...requiredComponents.map((component) => manifest.images[component]),
    ...optionalComponents.map((component) => manifest.images[component] ?? ""),
    manifest.manifestSha256,
  ].join("\n") + "\n");
}

function inspectState(path) {
  const state = loadState(resolve(path), true);
  if (!state) {
    process.stdout.write("false\n\n\n\n\n");
    return;
  }
  process.stdout.write([
    "true",
    state.activeColor,
    state.active.revision,
    state.previous ? (state.activeColor === "blue" ? "green" : "blue") : "",
    state.previous?.revision ?? "",
  ].join("\n") + "\n");
}

function previousManifestHash(path) {
  const state = loadState(resolve(path));
  if (!state.previous?.manifestSha256) fail("no verified previous release is recorded");
  process.stdout.write(`${state.previous.manifestSha256}\n`);
}

function writeDeployEnvironment(statePath, manifestPath, idleColor, environmentPath) {
  if (!["blue", "green"].includes(idleColor)) fail("idle color must be blue or green");
  const manifest = loadManifest(manifestPath);
  const candidate = releaseFromManifest(manifest);
  const state = loadState(resolve(statePath), true);
  if (state && state.activeColor === idleColor) fail("candidate color is active, not idle");

  const existing = state?.active ?? candidate;
  const blue = idleColor === "blue" ? candidate : existing;
  const green = idleColor === "green" ? candidate : existing;
  writeComposeEnvironment(environmentPath, blue, green, candidate.images.migrator);
}

function writeRollbackEnvironment(statePath, manifestPath, environmentPath) {
  const state = loadState(resolve(statePath));
  const manifest = loadManifest(manifestPath);
  requirePrevious(state, manifest);
  const previous = releaseFromManifest(manifest);
  const previousColor = state.activeColor === "blue" ? "green" : "blue";
  const blue = previousColor === "blue" ? previous : state.active;
  const green = previousColor === "green" ? previous : state.active;
  writeComposeEnvironment(environmentPath, blue, green, previous.images.migrator);
}

function commitDeploy(statePath, manifestPath, color) {
  if (!["blue", "green"].includes(color)) fail("active color must be blue or green");
  const priorState = loadState(resolve(statePath), true);
  if (priorState?.activeColor === color) fail("refusing to commit a deployment over the active color");
  const manifest = loadManifest(manifestPath);
  const active = releaseFromManifest(manifest);
  writeState(statePath, {
    schemaVersion: 1,
    activeColor: color,
    active,
    previous: priorState?.active ?? null,
    history: appendHistory(priorState, {
      action: "deploy",
      fromColor: priorState?.activeColor ?? null,
      fromRevision: priorState?.active?.revision ?? null,
      toColor: color,
      toRevision: active.revision,
      manifestSha256: active.manifestSha256,
    }),
  });
}

function commitRollback(statePath, manifestPath) {
  const priorState = loadState(resolve(statePath));
  const manifest = loadManifest(manifestPath);
  requirePrevious(priorState, manifest);
  const restored = releaseFromManifest(manifest);
  const color = priorState.activeColor === "blue" ? "green" : "blue";
  writeState(statePath, {
    schemaVersion: 1,
    activeColor: color,
    active: restored,
    previous: priorState.active,
    history: appendHistory(priorState, {
      action: "rollback",
      fromColor: priorState.activeColor,
      fromRevision: priorState.active.revision,
      toColor: color,
      toRevision: restored.revision,
      manifestSha256: restored.manifestSha256,
    }),
  });
}

function createLocalManifest(path, revision, api, next, migrator) {
  if (!releaseLabelPattern.test(revision)) fail("local revision is not a safe release label");
  const imageEntries = [
    ["api", api],
    ["next", next],
    ["migrator", migrator],
  ].map(([component, reference]) => {
    validateReference(reference, component);
    return { component, reference, digest: reference };
  });
  writeAtomic(path, `${JSON.stringify({
    schemaVersion: 1,
    revision,
    source: "local-content-addressed-build",
    compatibility: {
      databaseStrategy: "expand-migrate-contract",
      rollbackSupported: true,
      jwtKeyScope: "shared-vault",
      messageEnvelopeVersions: [1],
    },
    images: imageEntries,
  }, null, 2)}\n`);
}

const [command, ...args] = process.argv.slice(2);
switch (command) {
  case "inspect-manifest":
    if (args.length !== 1) fail("usage: inspect-manifest MANIFEST");
    inspectManifest(args[0]);
    break;
  case "inspect-state":
    if (args.length !== 1) fail("usage: inspect-state STATE");
    inspectState(args[0]);
    break;
  case "verify-publish-run":
    if (args.length !== 4) fail("usage: verify-publish-run RUN_JSON REPOSITORY RUN_ID REVISION");
    verifyPublishRun(...args);
    break;
  case "verify-published-manifest":
    if (args.length !== 5) {
      fail("usage: verify-published-manifest MANIFEST SOURCE_URL REPOSITORY RUN_ID REVISION");
    }
    verifyPublishedManifest(...args);
    break;
  case "previous-manifest-hash":
    if (args.length !== 1) fail("usage: previous-manifest-hash STATE");
    previousManifestHash(args[0]);
    break;
  case "write-deploy-env":
    if (args.length !== 4) fail("usage: write-deploy-env STATE MANIFEST IDLE_COLOR ENV_FILE");
    writeDeployEnvironment(...args);
    break;
  case "write-rollback-env":
    if (args.length !== 3) fail("usage: write-rollback-env STATE MANIFEST ENV_FILE");
    writeRollbackEnvironment(...args);
    break;
  case "commit-deploy":
    if (args.length !== 3) fail("usage: commit-deploy STATE MANIFEST COLOR");
    commitDeploy(...args);
    break;
  case "commit-rollback":
    if (args.length !== 2) fail("usage: commit-rollback STATE MANIFEST");
    commitRollback(...args);
    break;
  case "create-local":
    if (args.length !== 5) fail("usage: create-local MANIFEST REVISION API_ID NEXT_ID MIGRATOR_ID");
    createLocalManifest(...args);
    break;
  default:
    fail("expected inspect-manifest, inspect-state, verify-publish-run, verify-published-manifest, previous-manifest-hash, write-deploy-env, write-rollback-env, commit-deploy, commit-rollback, or create-local");
}
