using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WretchedWhispers.Api.Configuration;

public static class OpenTelemetryConfiguration
{
    public static WebApplicationBuilder AddWretchedWhispersOpenTelemetry(this WebApplicationBuilder builder)
    {
        // Enable Semantic Kernel diagnostic telemetry (including sensitive data for dev)
        AppContext.SetSwitch(
            "Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("WretchedWhispers.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSource("Microsoft.SemanticKernel*")
                .AddSource("WretchedWhispers.GameTurn")
                .AddOtlpExporter()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("Microsoft.SemanticKernel*")
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        return builder;
    }
}
