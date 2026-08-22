#r "../src/WasmFcs.Core/bin/Release/net10.0/WasmFcs.Core.dll"

open System
open System.Threading.Tasks
open WasmFcs.Core

let awaitTask (task: Task<string>) = task.GetAwaiter().GetResult()

let source = "printfn \"hello\"\nlet answer = 40 + 2\nprintfn \"%d\" answer"
let parse = WasmFcsApi.ParseJson(source, "/virtual/Smoke.fsx") |> awaitTask
let metadata = WasmFcsApi.MetadataJson(source, "/virtual/Smoke.fsx") |> awaitTask
let run = WasmFcsApi.RunJson(source, "/virtual/Smoke.fsx") |> awaitTask
let runSync = WasmFcsApi.RunJsonSync(source, "/virtual/Smoke.fsx")
let benchmark = WasmFcsApi.BenchmarkJsonSync("parse", source, "/virtual/Smoke.fsx")

if not (parse.Contains("implementation", StringComparison.Ordinal)) then failwith parse
if not (metadata.Contains("answer", StringComparison.Ordinal)) then failwith metadata
if not (run.Contains("hello", StringComparison.Ordinal)) then failwith run
if not (runSync.Contains("hello", StringComparison.Ordinal)) then failwith runSync
if not (benchmark.Contains("parseAndCheckMs", StringComparison.Ordinal)) then failwith benchmark
let denied = WasmFcsApi.RunJson("open System.IO\nprintfn (File.Exists \"secret\")", "/virtual/Denied.fsx") |> awaitTask
if not (denied.Contains("FCSW0101", StringComparison.Ordinal)) then failwith denied
printfn "%s" run
