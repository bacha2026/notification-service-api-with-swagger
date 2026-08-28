import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const probe = resolve(fileURLToPath(new URL("../cutover-probe.mjs", import.meta.url)));

async function listen(handler) {
  const server = createServer(handler);
  await new Promise((resolveListen) => server.listen(0, "127.0.0.1", resolveListen));
  const { port } = server.address();
  return { server, url: `http://127.0.0.1:${port}` };
}

async function runProbe(url, fromColor, fromSha, toColor, toSha) {
  const child = spawn(process.execPath, [probe, url, fromColor, fromSha, toColor, toSha, "1200"]);
  let stdout = "";
  let stderr = "";
  child.stdout.setEncoding("utf8").on("data", (chunk) => { stdout += chunk; });
  child.stderr.setEncoding("utf8").on("data", (chunk) => { stderr += chunk; });
  const status = await new Promise((resolveExit) => child.on("exit", resolveExit));
  return { status, stdout, stderr };
}

test("continuous probe accepts only the declared old-to-new transition", async (context) => {
  let requests = 0;
  let everyRequestUsedFreshConnection = true;
  const fromSha = "a".repeat(40);
  const toSha = "b".repeat(40);
  const { server, url } = await listen((request, response) => {
    requests += 1;
    everyRequestUsedFreshConnection &&= request.headers.connection === "close";
    const old = requests <= 4;
    const color = old ? "blue" : "green";
    const sha = old ? fromSha : toSha;
    response.writeHead(200, {
      "content-type": "application/json",
      "x-release-color": color,
      "x-release-sha": sha,
    });
    response.end(JSON.stringify({ service: "next", color, sha }));
  });
  context.after(() => server.close());

  const result = await runProbe(url, "blue", fromSha, "green", toSha);
  assert.equal(result.status, 0, result.stderr || result.stdout);
  const evidence = JSON.parse(result.stdout);
  assert.equal(evidence.result, "pass");
  assert.equal(evidence.failures.length, 0);
  assert.equal(everyRequestUsedFreshConnection, true);
  assert.deepEqual(evidence.observations.map(({ color }) => color), ["blue", "green"]);
});

test("continuous probe fails closed on any unexpected release response", async (context) => {
  const toSha = "c".repeat(40);
  const { server, url } = await listen((request, response) => {
    response.writeHead(200, {
      "content-type": "application/json",
      "x-release-color": "red",
      "x-release-sha": "wrong",
    });
    response.end(JSON.stringify({ service: "next", color: "red", sha: "wrong" }));
  });
  context.after(() => server.close());

  const result = await runProbe(url, "", "", "green", toSha);
  assert.notEqual(result.status, 0);
  const evidence = JSON.parse(result.stdout);
  assert.equal(evidence.result, "fail");
  assert.ok(evidence.failures.length > 0);
});
