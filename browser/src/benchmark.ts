import { createBrowserProgram, decode, type BrowserProgram } from "./runtime.js";

type BenchmarkTiming = {
  totalMs: number;
  parseAndCheckMs: number;
  symbolExtractionMs: number;
  compileMs: number;
  loadAndExecuteMs: number;
};

type BenchmarkResult = {
  operation: string;
  fileName: string;
  resultJson: string;
  timing: BenchmarkTiming;
};

type BenchmarkRequest = {
  scenario: "ready" | "cold" | "warm" | "steady";
  operation: "parse" | "metadata" | "run";
  source: string;
  fileName: string;
  iterations: number;
};

type BrowserBenchmarkResult = {
  readyMs: number;
  totalMs?: number;
  samplesMs: number[];
  inner: BenchmarkResult[];
};

declare global {
  interface Window {
    runFcsBenchmark(request: BenchmarkRequest): Promise<BrowserBenchmarkResult>;
  }
}

async function runFcsBenchmark(request: BenchmarkRequest): Promise<BrowserBenchmarkResult> {
  const readyStarted = performance.now();
  const program = await createBrowserProgram({ runtimeUrl: "/fcs-runtime" });
  const readyMs = performance.now() - readyStarted;

  if (request.scenario === "ready") {
    return { readyMs, samplesMs: [], inner: [] };
  }

  const call = () => benchmark(program, request.operation, request.source, request.fileName);
  if (request.scenario === "steady") await call();

  const samplesMs: number[] = [];
  const inner: BenchmarkResult[] = [];
  const count = request.scenario === "steady" ? request.iterations : 1;
  for (let index = 0; index < count; index += 1) {
    const started = performance.now();
    inner.push(await call());
    samplesMs.push(performance.now() - started);
  }

  return {
    readyMs,
    totalMs: request.scenario === "cold" ? readyMs + samplesMs[0] : undefined,
    samplesMs,
    inner,
  };
}

window.runFcsBenchmark = runFcsBenchmark;

async function benchmark(program: BrowserProgram, operation: BenchmarkRequest["operation"], source: string, fileName: string): Promise<BenchmarkResult> {
  if (typeof program.Benchmark !== "function") {
    throw new Error("WasmFcs.Browser.Program.Benchmark was not exported by the runtime.");
  }
  return decode<BenchmarkResult>(await program.Benchmark(operation, source, fileName));
}
