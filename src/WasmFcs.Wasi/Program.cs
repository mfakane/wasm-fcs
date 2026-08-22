using System.Text.Json;
using WasmFcs.Core;

namespace WasmFcs.Wasi;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            WasmFcsApi.ConfigureReferencePack(typeof(Program).Assembly);

            if (args.Length > 0 && args[0] == "--benchmark")
                return RunBenchmark();

            if (args.Length > 0 && args[0] is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (args.Length > 0 && args[0] == "--version")
            {
                Console.WriteLine("wasm-fcs 0.1.0");
                return 0;
            }

            if (args.Length > 0 && args[0] is "run" or "parse" or "metadata")
            {
                var fileName = args.Length > 1 ? args[1] : "-";
                var source = ReadSource(fileName);
                Console.WriteLine(Invoke(args[0], source, VirtualName(fileName)));
                return 0;
            }

            // The dotnet.wasm host reserves additional command-line options. The WASI
            // entrypoint therefore uses a stdin protocol when launched by Wasmtime:
            // either one JSON object or a command line followed by the source text.
            return RunRequest();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"wasm-fcs: {error.Message}");
            return 3;
        }
    }

    private static int RunRequest()
    {
        var firstLine = Console.ReadLine();
        if (firstLine == "@fcs-benchmark")
            return RunBenchmark();

        var input = firstLine is null ? "" : firstLine + "\n" + Console.In.ReadToEnd();
        string? command;
        string? source;
        string? fileName;
        if (input.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;
            command = root.TryGetProperty("command", out var commandValue) ? commandValue.GetString() : null;
            source = root.TryGetProperty("source", out var sourceValue) ? sourceValue.GetString() : null;
            fileName = root.TryGetProperty("fileName", out var fileValue) ? fileValue.GetString() : null;
        }
        else
        {
            var separator = input.IndexOf('\n');
            command = separator < 0 ? input.Trim() : input[..separator].Trim();
            source = separator < 0 ? "" : input[(separator + 1)..];
            fileName = "/virtual/Script.fsx";
        }

        if (command is not ("run" or "parse" or "metadata") || source is null)
        {
            Console.Error.WriteLine("stdin expects JSON {\"command\":\"run|parse|metadata\",\"source\":\"...\"} or command\\nsource.");
            return 2;
        }

        Console.WriteLine(Invoke(command, source, fileName ?? "/virtual/Script.fsx"));
        return 0;
    }

    private static int RunBenchmark()
    {
        Console.WriteLine("{\"event\":\"ready\"}");
        Console.Out.Flush();

        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var operation = root.GetProperty("operation").GetString() ?? "";
                var source = root.GetProperty("source").GetString() ?? "";
                var fileName = root.TryGetProperty("fileName", out var fileNameValue)
                    ? fileNameValue.GetString() ?? "/virtual/Benchmark.fsx"
                    : "/virtual/Benchmark.fsx";
                var previousOut = Console.Out;
                string response;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    response = WasmFcsApi.BenchmarkJsonSync(operation, source, fileName);
                }
                finally
                {
                    Console.SetOut(previousOut);
                }
                Console.WriteLine(response);
            }
            catch (Exception error)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { @event = "error", message = error.Message }));
            }
            Console.Out.Flush();
        }

        return 0;
    }

    private static string Invoke(string command, string source, string fileName) => command switch
    {
        "parse" => WasmFcsApi.ParseJsonSync(source, fileName),
        "metadata" => WasmFcsApi.MetadataJsonSync(source, fileName),
        "run" => WasmFcsApi.RunJsonSync(source, fileName),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private static string ReadSource(string fileName)
    {
        if (fileName == "-") return Console.In.ReadToEnd();
        return File.ReadAllText(fileName);
    }

    private static string VirtualName(string fileName) => fileName == "-" ? "/virtual/Script.fsx" : $"/virtual/{Path.GetFileName(fileName)}";

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: wasm-fcs <run|parse|metadata> [script.fsx|-]");
        Console.Error.WriteLine("       stdin: JSON request or command followed by source text");
        Console.Error.WriteLine("Run with: wasm-fcs-runtime/run-wasm-fcs run script.fsx");
    }
}
