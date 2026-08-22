namespace WasmFcs.Core

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Reflection
open System.Runtime.CompilerServices
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.IO
open FSharp.Compiler.Syntax
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

[<CLIMutable>]
type Diagnostic = {
    severity: string
    code: string
    message: string
    startLine: int
    startColumn: int
    endLine: int
    endColumn: int
}

[<CLIMutable>]
type SymbolMetadata = {
    name: string
    fullName: string
    kind: string
    typeText: string
    startLine: int
    startColumn: int
    endLine: int
    endColumn: int
    isDefinition: bool
}

[<CLIMutable>]
type ParseResult = {
    success: bool
    fileName: string
    treeKind: string
    declarationKinds: string list
    diagnostics: Diagnostic list
}

[<CLIMutable>]
type MetadataResult = {
    success: bool
    fileName: string
    symbols: SymbolMetadata list
    diagnostics: Diagnostic list
}

[<CLIMutable>]
type RunResult = {
    success: bool
    fileName: string
    output: string
    error: string
    durationMs: double
    diagnostics: Diagnostic list
}

[<CLIMutable>]
type BenchmarkTiming = {
    totalMs: double
    parseAndCheckMs: double
    symbolExtractionMs: double
    compileMs: double
    loadAndExecuteMs: double
}

[<CLIMutable>]
type BenchmarkResult = {
    operation: string
    fileName: string
    resultJson: string
    timing: BenchmarkTiming
}

type private CheckedSource = {
    parseTree: ParsedInput
    symbols: SymbolMetadata list
    diagnostics: Diagnostic list
    parseAndCheckMs: double
    symbolExtractionMs: double
}

type private CapturingStream(onDisposed: byte[] -> unit) =
    inherit MemoryStream()

    override this.Dispose(disposing) =
        if disposing then onDisposed (this.ToArray())
        base.Dispose(disposing)

type private MemoryFileSystem(sourcePath: string, source: byte[], outputPath: string, references: (string * byte[]) list) =
    inherit DefaultFileSystem()
    let mutable outputBytes = None

    let samePath left right =
        String.Equals(Path.GetFullPath left, Path.GetFullPath right, StringComparison.Ordinal)

    let virtualFiles = sourcePath :: outputPath :: (references |> List.map fst)
    let virtualDirectories =
        virtualFiles
        |> List.choose (fun path -> Path.GetDirectoryName path |> Option.ofObj)
        |> List.distinct

    member _.OutputBytes = outputBytes

    override _.OpenFileForReadShim(path, ?useMemoryMappedFile, ?shouldShadowCopy) =
        if samePath path sourcePath then
            new MemoryStream(source, writable = false) :> Stream
        else
            match references |> List.tryFind (fun (referencePath, _) -> samePath path referencePath) with
            | Some(_, bytes) -> new MemoryStream(bytes, writable = false) :> Stream
            | None ->
                base.OpenFileForReadShim(path, ?useMemoryMappedFile = useMemoryMappedFile, ?shouldShadowCopy = shouldShadowCopy)

    override _.OpenFileForWriteShim(path, ?fileMode, ?fileAccess, ?fileShare) =
        if samePath path outputPath then
            new CapturingStream(fun bytes -> outputBytes <- Some bytes) :> Stream
        else
            base.OpenFileForWriteShim(path, ?fileMode = fileMode, ?fileAccess = fileAccess, ?fileShare = fileShare)

    override _.FileExistsShim(path) =
        (virtualFiles |> List.exists (samePath path)) || base.FileExistsShim path

    override _.DirectoryExistsShim(path) =
        (virtualDirectories |> List.exists (samePath path)) || base.DirectoryExistsShim path

    override _.DirectoryCreateShim(path) =
        if virtualDirectories |> List.exists (samePath path) then path else base.DirectoryCreateShim path

    override _.FileDeleteShim(path) =
        if not (virtualFiles |> List.exists (samePath path)) then base.FileDeleteShim path

[<RequireQualifiedAccess>]
module private Engine =
    let private checker = FSharpChecker.Create(keepAssemblyContents = true, parallelReferenceResolution = false)
    let private gate = new SemaphoreSlim(1, 1)
    let private maxSourceLength = 256 * 1024
    let private jsonOptions = JsonSerializerOptions(JsonSerializerDefaults.Web)
    let mutable private referencePack: (string * byte[]) list = []
    let mutable private referencePackConfigured = false

    // FCS ships an internal immediate runner specifically for compiler-hosted
    // environments. Calling it through reflection keeps the public API small
    // while avoiding Task/ThreadPool waits that are unavailable in WASI.
    let private runImmediate<'T> (computation: Async<'T>) =
        let owner = typeof<FSharpChecker>.Assembly.GetType("Internal.Utilities.Library.PervasiveAutoOpens")
        if isNull owner then invalidOp "FCS immediate runner type is unavailable."
        let runner = owner.GetMethod("Async.RunImmediate.Static", BindingFlags.NonPublic ||| BindingFlags.Static)
        if isNull runner then invalidOp "FCS immediate runner method is unavailable."
        runner.MakeGenericMethod(typeof<'T>).Invoke(null, [| box computation; null |]) |> unbox<'T>

    let private json value = JsonSerializer.Serialize(value, jsonOptions)

    let private diagnostic severity code message startLine startColumn endLine endColumn =
        { severity = severity
          code = code
          message = message
          startLine = max 1 startLine
          startColumn = max 1 startColumn
          endLine = max 1 endLine
          endColumn = max 1 endColumn }

    let private fcsDiagnostic (item: FSharpDiagnostic) =
        diagnostic
            (if item.Severity = FSharpDiagnosticSeverity.Error then "error" else "warning")
            ($"FS{item.ErrorNumber:D4}")
            item.Message
            item.StartLine
            (item.StartColumn + 1)
            item.EndLine
            (item.EndColumn + 1)

    let private error code message = diagnostic "error" code message 1 1 1 1

    let private diagnosticsHaveErrors diagnostics =
        diagnostics |> List.exists (fun item -> item.severity = "error")

    let private validFileName (fileName: string) =
        let value = if String.IsNullOrWhiteSpace fileName then "/virtual/Script.fsx" else fileName
        let normalized = value.Replace('\\', '/')
        if not (normalized.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase)) then
            invalidArg (nameof fileName) "fileName must end with .fsx"
        if normalized |> Seq.exists Char.IsControl then
            invalidArg (nameof fileName) "fileName must not contain control characters"
        if normalized.Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.contains ".." then
            invalidArg (nameof fileName) "fileName must not contain parent-directory segments"
        if normalized.StartsWith("/virtual/", StringComparison.Ordinal) then normalized else $"/virtual/{Path.GetFileName normalized}"

    let private sourceDiagnostics (source: string) =
        [ if isNull source || String.IsNullOrWhiteSpace source then
              yield error "FCSW0001" "Source must not be empty."
          elif source.Length > maxSourceLength then
              yield error "FCSW0002" $"Source exceeds the {maxSourceLength} character limit."
          if not (isNull source) then
              for line in source.Replace("\r\n", "\n").Split('\n') do
                  let trimmed = line.TrimStart()
                  for directive in [ "#r"; "#load"; "#I"; "#line" ] do
                      if trimmed.StartsWith(directive, StringComparison.Ordinal)
                         && (trimmed.Length = directive.Length || Char.IsWhiteSpace trimmed[directive.Length]) then
                          yield error "FCSW0100" $"Directive {directive} is disabled in the sandbox." ]

    let private forbiddenSymbols =
        [ "System.IO."; "System.Net."; "System.Reflection."; "System.Diagnostics."; "Microsoft.Win32.";
          "System.Runtime.InteropServices."; "System.Runtime.Loader."; "System.Linq.Expressions.";
          "Microsoft.FSharp.NativeInterop."; "Microsoft.FSharp.Reflection." ]

    let private forbiddenExact =
        [ "System.Environment"; "System.Type"; "System.Object.GetType"; "System.Activator";
          "System.Console.OpenStandardInput"; "System.Console.OpenStandardOutput"; "System.Console.OpenStandardError";
          "System.Console.SetIn"; "System.Console.SetOut"; "System.Console.SetError" ]

    let private capabilityDiagnostics (uses: FSharpSymbolUse array) =
        uses
        |> Array.choose (fun useSite ->
            let name = useSite.Symbol.FullName
            let denied =
                not (String.IsNullOrWhiteSpace name)
                && (forbiddenSymbols |> List.exists (fun prefix -> name.StartsWith(prefix, StringComparison.Ordinal))
                    || forbiddenExact |> List.exists (fun value -> name = value || name.StartsWith(value + ".", StringComparison.Ordinal)))
            if denied then
                Some(error "FCSW0101" $"Sandbox capability denied: {name}")
            else None)
        |> Array.distinct
        |> Array.toList

    let private typeText (symbol: FSharpSymbol) =
        match symbol with
        | :? FSharpMemberOrFunctionOrValue as value -> value.FullType.Format(FSharpDisplayContext.Empty)
        | _ -> ""

    let private symbolMetadata (useSite: FSharpSymbolUse) =
        let symbol = useSite.Symbol
        let range = useSite.Range
        { name = symbol.DisplayName
          fullName = symbol.FullName
          kind = symbol.GetType().Name
          typeText = typeText symbol
          startLine = range.StartLine
          startColumn = range.StartColumn + 1
          endLine = range.EndLine
          endColumn = range.EndColumn + 1
          isDefinition = useSite.IsFromDefinition }

    let private defaultReferencePaths () =
        let runtimeDirectory = Path.GetDirectoryName(typeof<obj>.Assembly.Location) |> Option.ofObj
        let runtime =
            match runtimeDirectory with
            | Some directory when Directory.Exists directory -> Directory.GetFiles(directory, "*.dll") |> Array.toList
            | _ -> []
        let fsharpCore = typeof<list<int>>.Assembly.Location
        (fsharpCore :: runtime) |> List.filter (fun path -> not (String.IsNullOrWhiteSpace path)) |> List.distinct

    let private references () =
        if referencePackConfigured then referencePack |> List.map fst else defaultReferencePaths () |> List.map (fun path -> path)

    let configureReferencePackFromAssembly (assembly: Assembly) =
        if isNull assembly then nullArg (nameof assembly)
        let prefix = "WasmFcs.Reference."
        let suffix = ".reference.dll"
        let readResource name =
            use stream = assembly.GetManifestResourceStream name |> Option.ofObj |> Option.defaultWith (fun () -> invalidOp $"Missing reference resource: {name}")
            use buffer = new MemoryStream()
            stream.CopyTo buffer
            buffer.ToArray()
        let resources =
            assembly.GetManifestResourceNames()
            |> Array.choose (fun name ->
                if name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal) then
                    let shortName = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length) + ".dll"
                    Some($"/virtual/reference/{shortName}", readResource name)
                else None)
            |> Array.toList
        if not resources.IsEmpty then
            referencePack <- resources
            referencePackConfigured <- true

    let private projectOptions sourcePath =
        let otherOptions =
            [ "--nologo"; "--targetprofile:netcore"; "--noframework" ]
            @ (references () |> List.map (fun path -> $"-r:{path}"))
        { ProjectFileName = sourcePath
          ProjectId = None
          SourceFiles = [| sourcePath |]
          OtherOptions = otherOptions |> List.toArray
          ReferencedProjects = [||]
          IsIncompleteTypeCheckEnvironment = false
          UseScriptResolutionRules = false
          LoadTime = DateTime.MaxValue
          UnresolvedReferences = None
          OriginalLoadReferences = []
          Stamp = None }

    let private declarations (parseTree: ParsedInput) =
        match parseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            modules
            |> List.collect (fun (SynModuleOrNamespace(decls = values)) -> values)
            |> List.map (fun value -> value.GetType().Name)
        | ParsedInput.SigFile _ -> [ "SignatureFile" ]

    let private analyze (sourcePath: string) (source: string) = async {
        let fileSystem = MemoryFileSystem(sourcePath, Encoding.UTF8.GetBytes source, "/virtual/Analysis.dll", referencePack)
        let previous = FileSystemAutoOpens.FileSystem
        FileSystemAutoOpens.FileSystem <- fileSystem
        try
            let check = Stopwatch.StartNew()
            let! parseResult, checkAnswer = checker.ParseAndCheckFileInProject(sourcePath, 0, SourceText.ofString source, projectOptions sourcePath)
            check.Stop()
            let baseDiagnostics = parseResult.Diagnostics |> Array.toList |> List.map fcsDiagnostic
            match checkAnswer with
            | FSharpCheckFileAnswer.Succeeded checkResults ->
                let symbolsStarted = Stopwatch.StartNew()
                let uses = checkResults.GetAllUsesOfAllSymbolsInFile() |> Seq.toArray
                let symbols =
                    uses
                    |> Array.map symbolMetadata
                    |> Array.distinctBy (fun item -> item.fullName, item.startLine, item.startColumn, item.endLine, item.endColumn)
                    |> Array.toList
                symbolsStarted.Stop()
                return {
                    parseTree = parseResult.ParseTree
                    symbols = symbols
                    diagnostics = baseDiagnostics @ (checkResults.Diagnostics |> Array.toList |> List.map fcsDiagnostic) @ capabilityDiagnostics uses
                    parseAndCheckMs = check.Elapsed.TotalMilliseconds
                    symbolExtractionMs = symbolsStarted.Elapsed.TotalMilliseconds }
            | _ ->
                return {
                    parseTree = parseResult.ParseTree
                    symbols = []
                    diagnostics = baseDiagnostics @ [ error "FCSW0003" "F# type checking was interrupted." ]
                    parseAndCheckMs = check.Elapsed.TotalMilliseconds
                    symbolExtractionMs = 0.0 }
        finally
            FileSystemAutoOpens.FileSystem <- previous
    }

    let private prepareSource (source: string) =
        let hasModule =
            source.Replace("\r\n", "\n").Split('\n')
            |> Array.map (fun line -> line.TrimStart())
            |> Array.exists (fun line -> line.StartsWith("module ", StringComparison.Ordinal) || line.StartsWith("namespace ", StringComparison.Ordinal))
        if hasModule then source else "module WasmFcs.UserScript\n" + source

    let private compileAssembly (sourcePath: string) (outputPath: string) (source: string) = async {
        let referencesWithBytes = referencePack
        let fileSystem = MemoryFileSystem(sourcePath, Encoding.UTF8.GetBytes source, outputPath, referencesWithBytes)
        let previous = FileSystemAutoOpens.FileSystem
        FileSystemAutoOpens.FileSystem <- fileSystem
        try
            let args =
                Array.concat
                    [ [| "fsc.exe"; "--nologo"; "--target:library"; "--targetprofile:netcore"; "--noframework"; "-o"; outputPath |]
                      references () |> List.map (fun path -> $"-r:{path}") |> List.toArray
                      [| sourcePath |] ]
            let! compilerDiagnostics, compileException = checker.Compile args
            return compilerDiagnostics |> Array.toList |> List.map fcsDiagnostic, compileException, fileSystem.OutputBytes
        finally
            FileSystemAutoOpens.FileSystem <- previous
    }

    let rec private exceptionMessage (errorValue: exn) =
        match errorValue.InnerException with
        | null -> errorValue.Message
        | inner -> $"{errorValue.Message}: {exceptionMessage inner}"

    let private startupTypes (assembly: Assembly) =
        assembly.GetTypes()
        |> Array.filter (fun item ->
            let name = item.FullName |> Option.ofObj |> Option.defaultValue ""
            name.Contains("<StartupCode$", StringComparison.Ordinal))

    let private benchmarkTiming totalMs parseAndCheckMs symbolExtractionMs compileMs loadAndExecuteMs =
        { totalMs = totalMs
          parseAndCheckMs = parseAndCheckMs
          symbolExtractionMs = symbolExtractionMs
          compileMs = compileMs
          loadAndExecuteMs = loadAndExecuteMs }

    let private runSourceWithTiming (fileName: string) (source: string) = async {
        let started = Stopwatch.StartNew()
        let initial = sourceDiagnostics source
        if not initial.IsEmpty then
            started.Stop()
            return
                { success = false; fileName = fileName; output = ""; error = ""; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = initial },
                benchmarkTiming started.Elapsed.TotalMilliseconds 0.0 0.0 0.0 0.0
        else
            let! analysis = analyze fileName source
            if diagnosticsHaveErrors analysis.diagnostics then
                started.Stop()
                return
                    { success = false; fileName = fileName; output = ""; error = ""; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics },
                    benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs 0.0 0.0
            else
                let prepared = prepareSource source
                let compileStarted = Stopwatch.StartNew()
                let! compileDiagnostics, compileException, artifact = compileAssembly fileName "/virtual/Run.dll" prepared
                compileStarted.Stop()
                match compileException, artifact with
                | Some exceptionValue, _ ->
                    started.Stop()
                    return
                        { success = false; fileName = fileName; output = ""; error = exceptionMessage exceptionValue; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics @ compileDiagnostics @ [ error "FCSW0200" "F# compilation failed." ] },
                        benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs compileStarted.Elapsed.TotalMilliseconds 0.0
                | None, None ->
                    started.Stop()
                    return
                        { success = false; fileName = fileName; output = ""; error = ""; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics @ compileDiagnostics @ [ error "FCSW0201" "F# compiler did not produce an assembly." ] },
                        benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs compileStarted.Elapsed.TotalMilliseconds 0.0
                | None, Some bytes when diagnosticsHaveErrors compileDiagnostics ->
                    started.Stop()
                    return
                        { success = false; fileName = fileName; output = ""; error = ""; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics @ compileDiagnostics },
                        benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs compileStarted.Elapsed.TotalMilliseconds 0.0
                | None, Some bytes ->
                    let loadStarted = Stopwatch.StartNew()
                    try
                        let assembly = Assembly.Load bytes
                        let output = new StringWriter(CultureInfo.InvariantCulture)
                        let errorOutput = new StringWriter(CultureInfo.InvariantCulture)
                        let previousOut, previousError = Console.Out, Console.Error
                        Console.SetOut output
                        Console.SetError errorOutput
                        try
                            for item in startupTypes assembly do RuntimeHelpers.RunClassConstructor item.TypeHandle
                        finally
                            Console.SetOut previousOut
                            Console.SetError previousError
                        loadStarted.Stop()
                        started.Stop()
                        return
                            { success = true; fileName = fileName; output = output.ToString(); error = errorOutput.ToString(); durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics @ compileDiagnostics },
                            benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs compileStarted.Elapsed.TotalMilliseconds loadStarted.Elapsed.TotalMilliseconds
                    with errorValue ->
                        loadStarted.Stop()
                        started.Stop()
                        return
                            { success = false; fileName = fileName; output = ""; error = exceptionMessage errorValue; durationMs = started.Elapsed.TotalMilliseconds; diagnostics = analysis.diagnostics @ compileDiagnostics @ [ error "FCSW0202" "F# assembly execution failed." ] },
                            benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs compileStarted.Elapsed.TotalMilliseconds loadStarted.Elapsed.TotalMilliseconds
    }

    let private runSource (fileName: string) (source: string) = async {
        let! result, _ = runSourceWithTiming fileName source
        return result
    }

    let private parseSourceWithTiming (fileName: string) (source: string) = async {
        let started = Stopwatch.StartNew()
        let initial = sourceDiagnostics source
        if not initial.IsEmpty then
            started.Stop()
            return
                { success = false; fileName = fileName; treeKind = "none"; declarationKinds = []; diagnostics = initial },
                benchmarkTiming started.Elapsed.TotalMilliseconds 0.0 0.0 0.0 0.0
        else
            let! analysis = analyze fileName source
            let treeKind =
                match analysis.parseTree with
                | ParsedInput.ImplFile _ -> "implementation"
                | ParsedInput.SigFile _ -> "signature"
            let result =
                { success = not (diagnosticsHaveErrors analysis.diagnostics); fileName = fileName; treeKind = treeKind; declarationKinds = declarations analysis.parseTree; diagnostics = analysis.diagnostics }
            started.Stop()
            return
                result,
                benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs 0.0 0.0
    }

    let private parseSource (fileName: string) (source: string) = async {
        let! result, _ = parseSourceWithTiming fileName source
        return result
    }

    let private metadataSourceWithTiming (fileName: string) (source: string) = async {
        let started = Stopwatch.StartNew()
        let initial = sourceDiagnostics source
        if not initial.IsEmpty then
            started.Stop()
            return
                { success = false; fileName = fileName; symbols = []; diagnostics = initial },
                benchmarkTiming started.Elapsed.TotalMilliseconds 0.0 0.0 0.0 0.0
        else
            let! analysis = analyze fileName source
            let result =
                { success = not (diagnosticsHaveErrors analysis.diagnostics); fileName = fileName; symbols = analysis.symbols; diagnostics = analysis.diagnostics }
            started.Stop()
            return
                result,
                benchmarkTiming started.Elapsed.TotalMilliseconds analysis.parseAndCheckMs analysis.symbolExtractionMs 0.0 0.0
    }

    let private metadataSource (fileName: string) (source: string) = async {
        let! result, _ = metadataSourceWithTiming fileName source
        return result
    }

    let private withGate work = async {
        do! gate.WaitAsync() |> Async.AwaitTask
        try return! work () finally gate.Release() |> ignore
    }

    let parse fileName source = withGate (fun () -> parseSource (validFileName fileName) source)
    let metadata fileName source = withGate (fun () -> metadataSource (validFileName fileName) source)
    let run fileName source = withGate (fun () -> runSource (validFileName fileName) source)

    let serializeParse fileName source = task {
        let! result = parse fileName source |> Async.StartAsTask
        return json result
    }

    let serializeMetadata fileName source = task {
        let! result = metadata fileName source |> Async.StartAsTask
        return json result
    }

    let serializeRun fileName source = task {
        let! result = run fileName source |> Async.StartAsTask
        return json result
    }

    let private benchmark operation fileName source = withGate (fun () -> async {
        let normalizedFileName = validFileName fileName
        match operation with
        | "parse" ->
            let! result, timing = parseSourceWithTiming normalizedFileName source
            return { operation = operation; fileName = normalizedFileName; resultJson = json result; timing = timing }
        | "metadata" ->
            let! result, timing = metadataSourceWithTiming normalizedFileName source
            return { operation = operation; fileName = normalizedFileName; resultJson = json result; timing = timing }
        | "run" ->
            let! result, timing = runSourceWithTiming normalizedFileName source
            return { operation = operation; fileName = normalizedFileName; resultJson = json result; timing = timing }
        | _ -> return invalidArg (nameof operation) "operation must be parse, metadata, or run"
    })

    let serializeBenchmark operation fileName source = task {
        let! result = benchmark operation fileName source |> Async.StartAsTask
        return json result
    }

    let serializeParseSync fileName source = parse fileName source |> runImmediate |> json
    let serializeMetadataSync fileName source = metadata fileName source |> runImmediate |> json
    let serializeRunSync fileName source = run fileName source |> runImmediate |> json
    let serializeBenchmarkSync operation fileName source = benchmark operation fileName source |> runImmediate |> json

    let configureAssembly assembly = configureReferencePackFromAssembly assembly

[<AbstractClass; Sealed>]
type WasmFcsApi private () =
    static member ConfigureReferencePack(assembly: Assembly) = Engine.configureAssembly assembly
    static member ParseJson(source: string, fileName: string) = Engine.serializeParse fileName source
    static member MetadataJson(source: string, fileName: string) = Engine.serializeMetadata fileName source
    static member RunJson(source: string, fileName: string) = Engine.serializeRun fileName source
    static member BenchmarkJson(operation: string, source: string, fileName: string) = Engine.serializeBenchmark operation fileName source
    static member ParseJsonSync(source: string, fileName: string) = Engine.serializeParseSync fileName source
    static member MetadataJsonSync(source: string, fileName: string) = Engine.serializeMetadataSync fileName source
    static member RunJsonSync(source: string, fileName: string) = Engine.serializeRunSync fileName source
    static member BenchmarkJsonSync(operation: string, source: string, fileName: string) = Engine.serializeBenchmarkSync operation fileName source
