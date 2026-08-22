import { resolve } from "node:path";
import { existsSync } from "node:fs";
import { spawn, spawnSync } from "node:child_process";
import { chromium } from "../browser/node_modules/playwright/index.mjs";
import { workloads, workloadInfo } from "../bench/workloads.mjs";
import { round, summarize, summarizeInner } from "./benchmark-stats.mjs";

const root = resolve(import.meta.dirname, "..");
const browserRoot = resolve(root, "browser");
const suppliedUrl = process.env.BROWSER_URL;
const baseUrl = suppliedUrl ?? `http://127.0.0.1:${process.env.BROWSER_PORT ?? "4173"}`;
const iterations = Number(process.env.BENCH_ITERATIONS ?? "10");
const coldIterations = Number(process.env.BENCH_COLD_ITERATIONS ?? "3");
const warmIterations = Number(process.env.BENCH_WARM_ITERATIONS ?? "3");
const selectedWorkloads = new Set((process.env.BENCH_WORKLOADS ?? "small,medium,large").split(",").filter(Boolean));
const operations = ["parse", "metadata", "run"];
const scenarios = ["ready", "cold", "warm", "steady"];

let server;
let sharedBrowser;

async function waitForServer(url) {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    try {
      const response = await fetch(`${url}/benchmark.html`);
      if (response.ok) return;
    } catch {
      // The preview server is still starting.
    }
    await new Promise((resolveWait) => setTimeout(resolveWait, 100));
  }
  throw new Error(`Browser server did not become ready: ${url}`);
}

async function startServer() {
  if (suppliedUrl) return;
  if (!existsSync(resolve(browserRoot, "dist/benchmark.html"))) {
    const build = spawnSync("npm", ["run", "build", "--prefix", browserRoot], { cwd: root, stdio: "inherit" });
    if (build.status !== 0) throw new Error("Browser build failed.");
  }
  server = spawn("npm", ["run", "preview", "--prefix", browserRoot, "--", "--host", "127.0.0.1", "--port", baseUrl.split(":").pop(), "--strictPort"], {
    cwd: root,
    stdio: ["ignore", "pipe", "pipe"],
  });
  server.stdout.on("data", (data) => process.stderr.write(`[browser-server] ${data}`));
  server.stderr.on("data", (data) => process.stderr.write(`[browser-server] ${data}`));
  await waitForServer(baseUrl);
}

function makeRecord(workload, scenario, operation, samples, inner, extra = {}) {
  return {
    schemaVersion: 1,
    platform: "browser",
    workload: workload.name,
    scenario,
    operation,
    scope: "page-wall",
    ...summarize(samples),
    samplesMs: samples,
    inner: summarizeInner(inner),
    referenceCount: 10,
    ...workloadInfo(workload),
    ...extra,
  };
}

async function launchBrowser() {
  return chromium.launch({
    executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined,
  });
}

async function runScenario(workload, operation, scenario) {
  const runPage = async (iterationsForPage) => {
    const freshBrowser = scenario === "ready" || scenario === "cold";
    const browser = freshBrowser ? await launchBrowser() : (sharedBrowser ??= await launchBrowser());
    const started = performance.now();
    const page = await browser.newPage();
    try {
      await page.goto(`${baseUrl}/benchmark.html`, { waitUntil: "networkidle" });
      const result = await page.evaluate((request) => window.runFcsBenchmark(request), {
        scenario,
        operation,
        source: workload.source,
        fileName: workload.fileName,
        iterations: iterationsForPage,
      });
      return {
        ...result,
        processReadyMs: performance.now() - started,
        processTotalMs: performance.now() - started,
      };
    } finally {
      await page.close().catch(() => {});
      if (freshBrowser) await browser.close();
    }
  };

  if (scenario === "ready") {
    const samples = [];
    for (let index = 0; index < coldIterations; index += 1) samples.push((await runPage(0)).processReadyMs);
    return makeRecord(workload, scenario, "ready", samples, []);
  }

  if (scenario === "steady") {
    const result = await runPage(iterations);
    return makeRecord(workload, scenario, operation, result.samplesMs, result.inner, { readyMs: round(result.readyMs) });
  }

  const count = scenario === "cold" ? coldIterations : warmIterations;
  const samples = [];
  const inner = [];
  for (let index = 0; index < count; index += 1) {
    const result = await runPage(1);
    samples.push(scenario === "cold" ? result.processTotalMs : result.samplesMs[0]);
    inner.push(...result.inner);
  }
  return makeRecord(workload, scenario, operation, samples, inner);
}

try {
  await startServer();
  try {
    for (const workload of workloads.filter((item) => selectedWorkloads.has(item.name))) {
      for (const scenario of scenarios) {
        const ops = scenario === "ready" ? [null] : operations;
        for (const operation of ops) {
          console.log(JSON.stringify(await runScenario(workload, operation, scenario)));
        }
      }
    }
  } finally {
    if (sharedBrowser) await sharedBrowser.close();
  }
} finally {
  if (server) server.kill("SIGTERM");
}
