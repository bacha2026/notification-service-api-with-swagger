import { cpSync, existsSync } from "node:fs";
import path from "node:path";

const projectRoot = process.cwd();
const standaloneRoot = path.join(projectRoot, ".next", "standalone");

if (!existsSync(path.join(standaloneRoot, "server.js"))) {
  throw new Error("Standalone output is missing. Run `npm run build` before the browser tests.");
}

cpSync(path.join(projectRoot, "public"), path.join(standaloneRoot, "public"), {
  recursive: true,
  force: true,
});
cpSync(path.join(projectRoot, ".next", "static"), path.join(standaloneRoot, ".next", "static"), {
  recursive: true,
  force: true,
});

