using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Api.Deployment;

/// <summary>
/// The one non-serving mode this executable has: <c>dotnet run -- export-traces [outDir]</c> dumps
/// stored turn traces and exits. It needs the built container (repositories, DbContext), so it runs
/// after the host is built — but it is a CLI concern, and keeping it out of the middleware pipeline
/// is the point of this file.
/// </summary>
public static class TraceExportCommand
{
    private const string Verb = "export-traces";
    private const string DefaultOutputDirectory = "./traces-export";

    public static bool Matches(string[] args) => args is [Verb, ..];

    public static Task RunAsync(IServiceProvider services, string[] args) =>
        TraceExporter.ExportAsync(
            services, args.Length > 1 ? args[1] : DefaultOutputDirectory);
}
