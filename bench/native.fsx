#r "../src/WasmFcs.Core/bin/Release/net10.0/WasmFcs.Core.dll"

open System
open System.Diagnostics
open System.Threading.Tasks
open WasmFcs.Core

let source = """
module Example
open System
let answer = 40 + 2
printfn "answer = %d" answer
"""

let awaitTask (work: unit -> Task<string>) = work().GetAwaiter().GetResult() |> ignore

let measure name operation =
    awaitTask operation
    let stopwatch = Stopwatch.StartNew()
    for _ in 1 .. 10 do awaitTask operation
    stopwatch.Stop()
    printfn "{\"mode\":\"native\",\"operation\":\"%s\",\"iterations\":10,\"totalMs\":%.3f,\"meanMs\":%.3f}" name stopwatch.Elapsed.TotalMilliseconds (stopwatch.Elapsed.TotalMilliseconds / 10.0)

measure "parse" (fun () -> WasmFcsApi.ParseJson(source, "/virtual/Bench.fsx"))
measure "metadata" (fun () -> WasmFcsApi.MetadataJson(source, "/virtual/Bench.fsx"))
measure "run" (fun () -> WasmFcsApi.RunJson(source, "/virtual/Bench.fsx"))

