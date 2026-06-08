using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WretchedWhispers.Api.Configuration;

public static class OpenTelemetryConfiguration
{
    public static WebApplicationBuilder AddWretchedWhispersOpenTelemetry(this WebApplicationBuilder builder)
    {
        // Enable Microsoft.Extensions.AI / Agent Framework diagnostic telemetry,
        // including sensitive data (prompts/completions) for local dev.
        AppContext.SetSwitch(
            "Microsoft.Extensions.AI.OpenTelemetryConsumer.EnableSensitiveData", true);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("WretchedWhispers.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSource("Microsoft.Extensions.AI")
                .AddSource("Microsoft.Agents.AI")
                .AddSource("WretchedWhispers.GameTurn")
                .AddOtlpExporter()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("Microsoft.Extensions.AI")
                .AddMeter("Microsoft.Agents.AI")
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        return builder;
    }
}
