using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WretchedWhispers.Api.Configuration;

public static class OpenTelemetryConfiguration
{
    public static WebApplicationBuilder AddWretchedWhispersOpenTelemetry(this WebApplicationBuilder builder)
    {
        // Dev only: prompt/completion payloads and console trace dumps. Player input and LLM
        // output are sensitive data and must not reach exporters in production.
        var isDevelopment = builder.Environment.IsDevelopment();
        if (isDevelopment)
            AppContext.SetSwitch(
                "Microsoft.Extensions.AI.OpenTelemetryConsumer.EnableSensitiveData", true);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("WretchedWhispers.Api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Agents.AI")
                    .AddSource("WretchedWhispers.GameTurn")
                    .AddOtlpExporter();
                if (isDevelopment)
                    tracing.AddConsoleExporter();
            })
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
