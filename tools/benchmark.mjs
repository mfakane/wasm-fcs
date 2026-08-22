import { resolve } from "node:path";
import { existsSync } from "node:fs";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { workloads, workloadInfo } from "../bench/workloads.mjs";
import { round, summarize, summarizeInner } from "./benchmark-stats.mjs";

const root = resolve(import.meta.dirname, "..");
const runtime = process.env.WASM_FCS_RUNTIME;
const nativeHost = process.env.FCS_NATIVE_BENCH ?? resolve(root, "bench/WasmFcs.BenchHost/bin/Release/net10.0/WasmFcs.BenchHost.dll");
const iterations = Number(process.env.BENCH_ITERATIONS ?? "10");
const coldIterations = Number(process.env.BENCH_COLD_ITERATIONS ?? "3");
const warmIterations = Number(process.env.BENCH_WARM_ITERATIONS ?? "3");
const selectedWorkloads = new Set((process.env.BENCH_WORKLOADS ?? "small,medium,large").split(",").filter(Boolean));
const operations = ["parse", "metadata", "run"];
const scenarios = ["ready", "cold", "warm", "steady"];

if (!runtime) {
  console.error("Set WASM_FCS_RUNTIME inside nix develop to benchmark WASI.");
  process.exit(2);
}
if (!existsSync(nativeHost)) {
  console.error(`Build the native benchmark host first: ${nativeHost}`);
  process.exit(2);
}

function lineReader(stream) {
  const reader = createInterface({ input: stream });
  const queue = [];
  const waiters = [];
  let closed = false;
  reader.on("line", (line) => {
    const waiter = waiters.shift();
    if (waiter) waiter(line);
    else queue.push(line);
  });
  reader.on("close", () => {
    closed = true;
    while (waiters.length) waiters.shift()(null);
  });
  return {
    next() {
      if (queue.length) return Promise.resolve(queue.shift());
      if (closed) return Promise.resolve(null);
      return new Promise((resolveLine) => waiters.push(resolveLine));
    },
    close() {
      reader.close();
    },
  };
}

async function nextJson(reader) {
  while (true) {
    const line = await reader.next();
    if (line === null) throw new Error("Benchmark child exited before a JSON response.");
    try {
      return JSON.parse(line);
    } catch {
      // Keep protocol stdout usable if a runtime writes an informational line.
    }
  }
}

function waitForExit(child) {
  return new Promise((resolveExit) => {
    if (child.exitCode !== null) resolveExit();
    else child.once("close", resolveExit);
  });
}

async function startSession(platform) {
  const started = performance.now();
  const command = platform === "native" ? "dotnet" : "wasmtime";
  const args = platform === "native"
    ? [nativeHost]
    : ["run", "-S", "http", "--dir", ".", "dotnet.wasm", "WasmFcs.Wasi"];
  const child = spawn(command, args, {
    cwd: platform === "native" ? root : runtime,
    stdio: ["pipe", "pipe", "pipe"],
  });
  const reader = lineReader(child.stdout);
  child.stderr.on("data", (data) => process.stderr.write(`[${platform}] ${data}`));
  if (platform === "wasi") child.stdin.write("@fcs-benchmark\n");
  const ready = await nextJson(reader);
  if (ready.event !== "ready") throw new Error(`${platform} did not report ready: ${JSON.stringify(ready)}`);
  return { child, reader, started, readyMs: round(performance.now() - started) };
}

async function request(session, operation, source, fileName) {
  const started = performance.now();
  session.child.stdin.write(`${JSON.stringify({ operation, source, fileName })}\n`);
  const response = await nextJson(session.reader);
  if (response.event === "error") throw new Error(response.message);
  return { response, elapsedMs: round(performance.now() - started) };
}

async function stopSession(session) {
  session.child.stdin.end();
  await waitForExit(session.child);
  session.reader.close();
}

function fileNameFor(workload, scenario, index) {
  if (scenario === "warm") return workload.fileName.replace(/\.fsx$/i, `.warm-${index}.fsx`);
  return workload.fileName;
}

async function measure(platform, workload, operation, scenario) {
  if (scenario === "ready") {
    const samples = [];
    for (let index = 0; index < coldIterations; index += 1) {
      const session = await startSession(platform);
      samples.push(session.readyMs);
      await stopSession(session);
    }
    return makeRecord(platform, workload, scenario, "ready", samples, [], {});
  }

  if (scenario === "steady") {
    const session = await startSession(platform);
    await request(session, operation, workload.source, workload.fileName);
    const samples = [];
    const responses = [];
    for (let index = 0; index < iterations; index += 1) {
      const sample = await request(session, operation, workload.source, workload.fileName);
      samples.push(sample.elapsedMs);
      responses.push(sample.response);
    }
    await stopSession(session);
    return makeRecord(platform, workload, scenario, operation, samples, responses, { readyMs: session.readyMs });
  }

  const count = scenario === "cold" ? coldIterations : warmIterations;
  const samples = [];
  const responses = [];
  for (let index = 0; index < count; index += 1) {
    const session = await startSession(platform);
    const started = scenario === "cold" ? session.started : performance.now();
    const sample = await request(session, operation, workload.source, fileNameFor(workload, scenario, index));
    samples.push(round(performance.now() - started));
    responses.push(sample.response);
    await stopSession(session);
  }
  return makeRecord(platform, workload, scenario, operation, samples, responses, {});
}

function makeRecord(platform, workload, scenario, operation, samples, responses, extra) {
  const info = workloadInfo(workload);
  const stats = summarize(samples);
  return {
    schemaVersion: 1,
    platform,
    workload: workload.name,
    scenario,
    operation,
    scope: "host-wall",
    ...stats,
    samplesMs: samples,
    inner: summarizeInner(responses),
    referenceCount: 10,
    ...info,
    ...extra,
  };
}

for (const platform of ["native", "wasi"]) {
  for (const workload of workloads.filter((item) => selectedWorkloads.has(item.name))) {
    for (const scenario of scenarios) {
      const ops = scenario === "ready" ? [null] : operations;
      for (const operation of ops) {
        console.log(JSON.stringify(await measure(platform, workload, operation, scenario)));
      }
    }
  }
}
