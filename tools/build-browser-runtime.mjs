import { cpSync, existsSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const project = resolve(root, "src/WasmFcs.Browser/WasmFcs.Browser.csproj");
const publish = resolve(root, "src/WasmFcs.Browser/bin/Release/net10.0/publish/wwwroot");
const output = resolve(root, "browser/public/fcs-runtime");
const fcs = process.env.WASM_FCS_DLL ?? "";

const args = [
  "publish", project, "-c", "Release",
  "-p:RunAOTCompilation=false",
  ...(fcs ? [`-p:WasmFcsPath=${fcs}`] : []),
];
const result = spawnSync("dotnet", args, { cwd: root, stdio: "inherit" });
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status ?? 1);
if (!existsSync(resolve(publish, "_framework/dotnet.js"))) {
  console.error(`Browser runtime was not published at ${publish}`);
  process.exit(1);
}

rmSync(output, { recursive: true, force: true });
cpSync(publish, output, { recursive: true });
const nativeWasm = readdirSync(resolve(output, "_framework"))
  .find((name) => name.startsWith("dotnet.native.") && name.endsWith(".wasm"));
if (!nativeWasm) {
  console.error(`Browser native WASM was not published at ${output}`);
  process.exit(1);
}
console.log(`browser_runtime_bytes=${statSync(resolve(output, "_framework", nativeWasm)).size}`);
