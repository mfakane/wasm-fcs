using System.Text.Json;
using WasmFcs.Core;

WasmFcsApi.ConfigureReferencePack(typeof(Program).Assembly);
Console.WriteLine("{\"event\":\"ready\"}");
Console.Out.Flush();

string? line;
while ((line = Console.ReadLine()) is not null)
{
    try
    {
        using var request = JsonDocument.Parse(line);
        var root = request.RootElement;
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
