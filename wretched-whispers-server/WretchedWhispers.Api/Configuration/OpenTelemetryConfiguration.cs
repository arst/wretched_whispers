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

        // Exporting only makes sense when there is somewhere to export to. Unconditional OTLP means
        // the desktop and container builds retry localhost:4317 forever and log every failure.
        var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("WretchedWhispers.Api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Agents.AI")
                    .AddSource("WretchedWhispers.GameTurn");
                if (hasOtlpEndpoint)
                    tracing.AddOtlpExporter();
                if (isDevelopment)
                    tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddMeter("Microsoft.Extensions.AI")
                    .AddMeter("Microsoft.Agents.AI");
                if (hasOtlpEndpoint)
                    metrics.AddOtlpExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            // Without an exporter this pipeline collected every log record and dropped it on the
            // floor: traces and metrics reached the collector, logs never did.
            if (hasOtlpEndpoint)
                logging.AddOtlpExporter();
        });

        return builder;
    }
}
