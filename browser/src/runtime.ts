type DotnetInstance = {
  getAssemblyExports: (assemblyName: string) => Promise<Record<string, unknown>>;
  getConfig: () => { mainAssemblyName: string };
  runMain: () => Promise<unknown>;
};

type DotnetModule = {
  dotnet: {
    create: () => Promise<DotnetInstance>;
  };
};

export type BrowserProgram = {
  Status: () => Promise<string>;
  Parse: (source: string, fileName: string) => Promise<string>;
  Metadata: (source: string, fileName: string) => Promise<string>;
  Run: (source: string, fileName: string) => Promise<string>;
  Benchmark: (operation: string, source: string, fileName: string) => Promise<string>;
};

function programFromExports(exports: Record<string, unknown>): BrowserProgram {
  const wasmFcs = exports["WasmFcs"] as Record<string, unknown> | undefined;
  const browser = wasmFcs?.["Browser"] as Record<string, unknown> | undefined;
  const program = browser?.["Program"] as BrowserProgram | undefined;
  if (!program) throw new Error("WasmFcs.Browser.Program was not exported by the runtime.");
  return program;
}

async function loadDotnet(runtimeUrl: string): Promise<DotnetModule> {
  const base = runtimeUrl.replace(/\/+$/, "");
  return (await import(/* @vite-ignore */ `${base}/_framework/dotnet.js`)) as DotnetModule;
}

export async function createBrowserProgram(options: { runtimeUrl?: string } = {}): Promise<BrowserProgram> {
  if (globalThis.crossOriginIsolated !== true) {
    throw new Error("FCS Browser WASM requires Cross-Origin-Opener-Policy and Cross-Origin-Embedder-Policy.");
  }
  const runtime = await loadDotnet(options.runtimeUrl ?? "/fcs-runtime");
  const instance = await runtime.dotnet.create();
  await instance.runMain();
  const exports = await instance.getAssemblyExports(instance.getConfig().mainAssemblyName);
  const program = programFromExports(exports);
  const status = await program.Status();
  if (status !== "ready") throw new Error(`FCS Browser WASM is not ready: ${status}`);
  return program;
}

export function decode<T>(value: string): T {
  const parsed: unknown = JSON.parse(value);
  if (!parsed || typeof parsed !== "object") throw new Error("Browser runtime returned invalid JSON.");
  return parsed as T;
}
