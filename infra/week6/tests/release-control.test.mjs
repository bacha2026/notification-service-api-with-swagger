import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const script = resolve(fileURLToPath(new URL("../release-control.mjs", import.meta.url)));
const imageId = (character) => `sha256:${character.repeat(64)}`;

function command(...args) {
  return execFileSync(process.execPath, [script, ...args], { encoding: "utf8" });
}

test("a verified manifest produces color-specific immutable Compose values", () => {
  const directory = mkdtempSync(join(tmpdir(), "nsa-release-"));
  const state = join(directory, "state.json");
  const environment = join(directory, "release.env");
  const blueManifest = join(directory, "blue.json");
  const greenManifest = join(directory, "green.json");

  command("create-local", blueManifest, "a".repeat(40), imageId("1"), imageId("2"), imageId("3"));
  command("write-deploy-env", state, blueManifest, "blue", environment);
  command("commit-deploy", state, blueManifest, "blue");
  command("create-local", greenManifest, "b".repeat(40), imageId("4"), imageId("5"), imageId("6"));
  command("write-deploy-env", state, greenManifest, "green", environment);

  const values = readFileSync(environment, "utf8");
  assert.match(values, /BLUE_RELEASE_SHA=a{40}/);
  assert.match(values, /GREEN_RELEASE_SHA=b{40}/);
  assert.match(values, /MIGRATOR_IMAGE=sha256:6{64}/);

  command("commit-deploy", state, greenManifest, "green");
  const persisted = JSON.parse(readFileSync(state, "utf8"));
  assert.equal(persisted.activeColor, "green");
  assert.equal(persisted.previous.revision, "a".repeat(40));
  assert.deepEqual(persisted.history.map(({ action }) => action), ["deploy", "deploy"]);
});

test("rollback accepts only the exact recorded prior manifest and swaps state", () => {
  const directory = mkdtempSync(join(tmpdir(), "nsa-rollback-"));
  const state = join(directory, "state.json");
  const environment = join(directory, "release.env");
  const first = join(directory, "first.json");
  const second = join(directory, "second.json");
  const wrong = join(directory, "wrong.json");

  command("create-local", first, "c".repeat(40), imageId("1"), imageId("2"), imageId("3"));
  command("commit-deploy", state, first, "blue");
  command("create-local", second, "d".repeat(40), imageId("4"), imageId("5"), imageId("6"));
  command("commit-deploy", state, second, "green");
  command("create-local", wrong, "c".repeat(40), imageId("7"), imageId("8"), imageId("9"));

  const rejected = spawnSync(process.execPath, [script, "write-rollback-env", state, wrong, environment], { encoding: "utf8" });
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /not the exact previous manifest/);

  command("write-rollback-env", state, first, environment);
  command("commit-rollback", state, first);
  const persisted = JSON.parse(readFileSync(state, "utf8"));
  assert.equal(persisted.activeColor, "blue");
  assert.equal(persisted.active.revision, "c".repeat(40));
  assert.equal(persisted.previous.revision, "d".repeat(40));
  assert.equal(persisted.history.at(-1).action, "rollback");
});

test("rollback rejects state whose recorded images differ from the archived manifest", () => {
  const directory = mkdtempSync(join(tmpdir(), "nsa-tampered-rollback-"));
  const state = join(directory, "state.json");
  const environment = join(directory, "release.env");
  const first = join(directory, "first.json");
  const second = join(directory, "second.json");

  command("create-local", first, "1".repeat(40), imageId("1"), imageId("2"), imageId("3"));
  command("commit-deploy", state, first, "blue");
  command("create-local", second, "2".repeat(40), imageId("4"), imageId("5"), imageId("6"));
  command("commit-deploy", state, second, "green");

  const tampered = JSON.parse(readFileSync(state, "utf8"));
  tampered.previous.images.api = imageId("9");
  writeFileSync(state, JSON.stringify(tampered));

  const rejected = spawnSync(
    process.execPath,
    [script, "write-rollback-env", state, first, environment],
    { encoding: "utf8" },
  );
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /does not exactly match its archived manifest/);
});

test("deployment rejects malformed active release state before writing Compose values", () => {
  const directory = mkdtempSync(join(tmpdir(), "nsa-invalid-state-"));
  const state = join(directory, "state.json");
  const environment = join(directory, "release.env");
  const first = join(directory, "first.json");
  const second = join(directory, "second.json");

  command("create-local", first, "3".repeat(40), imageId("1"), imageId("2"), imageId("3"));
  command("commit-deploy", state, first, "blue");
  command("create-local", second, "4".repeat(40), imageId("4"), imageId("5"), imageId("6"));

  const malformed = JSON.parse(readFileSync(state, "utf8"));
  delete malformed.active.images.next;
  writeFileSync(state, JSON.stringify(malformed));

  const rejected = spawnSync(
    process.execPath,
    [script, "write-deploy-env", state, second, "green", environment],
    { encoding: "utf8" },
  );
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /active deployment state is missing required image 'next'/);
});

test("mutable image references and manifests without compatibility evidence fail closed", () => {
  const directory = mkdtempSync(join(tmpdir(), "nsa-invalid-release-"));
  const manifest = join(directory, "invalid.json");
  writeFileSync(manifest, JSON.stringify({
    schemaVersion: 1,
    revision: "e".repeat(40),
    images: [
      { component: "api", reference: "example/api:latest" },
      { component: "next", reference: imageId("2") },
      { component: "migrator", reference: imageId("3") },
    ],
  }));

  const result = spawnSync(process.execPath, [script, "inspect-manifest", manifest], { encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /databaseStrategy/);

  const repository = "example/notification-service";
  const source = `https://github.com/${repository}`;
  const runId = "123456789";
  const revision = "f".repeat(40);
  const runMetadata = join(directory, "run.json");
  const publishedManifest = join(directory, "published.json");
  const workflowRun = {
    id: runId,
    attempt: "1",
    url: `${source}/actions/runs/${runId}`,
  };
  const imageNames = {
    api: "notification-api",
    next: "notification-web",
    migrator: "notification-migrator",
    worker: "notification-worker",
    "outbox-worker": "notification-outbox-worker",
  };
  const images = Object.entries(imageNames).map(([component, name], index) => {
    const image = `ghcr.io/example/${name}`;
    const digest = imageId(String(index + 1));
    return {
      component,
      image,
      digest,
      reference: `${image}@${digest}`,
      tags: [`${image}:sha-${revision}`],
      revision,
      gitRef: "refs/heads/main",
      workflowRun,
      source,
      attestations: {
        sbom: true,
        buildkitProvenance: true,
        signedGitHubProvenance: true,
      },
    };
  });
  writeFileSync(runMetadata, JSON.stringify({
    id: Number(runId),
    repository: { full_name: repository },
    head_repository: { full_name: repository },
    path: ".github/workflows/publish.yml",
    event: "push",
    status: "completed",
    conclusion: "success",
    head_sha: revision,
  }));
  writeFileSync(publishedManifest, JSON.stringify({
    schemaVersion: 1,
    revision,
    gitRef: "refs/heads/main",
    workflowRun,
    source,
    compatibility: {
      databaseStrategy: "expand-migrate-contract",
      rollbackSupported: true,
      jwtKeyScope: "shared-vault",
      messageEnvelopeVersions: [1],
    },
    images,
  }));

  command("verify-publish-run", runMetadata, repository, runId, revision);
  command("verify-published-manifest", publishedManifest, source, repository, runId, revision);

  const untrustedRun = JSON.parse(readFileSync(runMetadata, "utf8"));
  untrustedRun.path = ".github/workflows/untrusted.yml";
  writeFileSync(runMetadata, JSON.stringify(untrustedRun));
  const rejectedRun = spawnSync(
    process.execPath,
    [script, "verify-publish-run", runMetadata, repository, runId, revision],
    { encoding: "utf8" },
  );
  assert.notEqual(rejectedRun.status, 0);
  assert.match(rejectedRun.stderr, /did not execute \.github\/workflows\/publish\.yml/);

  const untrustedManifest = JSON.parse(readFileSync(publishedManifest, "utf8"));
  untrustedManifest.images[0].image = "ghcr.io/attacker/notification-api";
  untrustedManifest.images[0].reference = `${untrustedManifest.images[0].image}@${untrustedManifest.images[0].digest}`;
  writeFileSync(publishedManifest, JSON.stringify(untrustedManifest));
  const rejectedManifest = spawnSync(
    process.execPath,
    [script, "verify-published-manifest", publishedManifest, source, repository, runId, revision],
    { encoding: "utf8" },
  );
  assert.notEqual(rejectedManifest.status, 0);
  assert.match(rejectedManifest.stderr, /outside the trusted GHCR namespace/);
});
