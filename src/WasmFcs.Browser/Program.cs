using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using WasmFcs.Core;

namespace WasmFcs.Browser;

public static partial class Program
{
    private static int configured;

    public static Task Main(string[] args)
    {
        Configure();
        return Task.CompletedTask;
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static Task<string> Status()
    {
        Configure();
        return Task.FromResult("ready");
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static Task<string> Parse(string source, string fileName)
    {
        Configure();
        return WasmFcsApi.ParseJson(source, fileName);
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static Task<string> Metadata(string source, string fileName)
    {
        Configure();
        return WasmFcsApi.MetadataJson(source, fileName);
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static Task<string> Run(string source, string fileName)
    {
        Configure();
        return WasmFcsApi.RunJson(source, fileName);
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static Task<string> Benchmark(string operation, string source, string fileName)
    {
        Configure();
        return WasmFcsApi.BenchmarkJson(operation, source, fileName);
    }

    private static void Configure()
    {
        if (Interlocked.Exchange(ref configured, 1) == 0)
            WasmFcsApi.ConfigureReferencePack(typeof(Program).Assembly);
    }
}
