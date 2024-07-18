using System.Diagnostics;
using Consumer;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().Enrich.FromLogContext()
    .Enrich.WithCorrelationId().WriteTo.Console(new RenderedCompactJsonFormatter()).CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Logging.AddOpenTelemetry(options =>
{
    options
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService("Consumer"));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Consumer"))
    .WithTracing(tracing => tracing
        .AddSource(nameof(ConsumerService))
        .AddAspNetCoreInstrumentation(options => { options.RecordException = true; })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpResponseMessage = (activity, response) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    var exceptionAttributes = new ActivityTagsCollection
                    {
                        { "http.status_code", (int)response.StatusCode },
                        { "http.response_message", response.ReasonPhrase }
                    };
                    activity?.AddEvent(new ActivityEvent("IsNotSuccessStatusCode", default, exceptionAttributes));
                }

                activity?.SetTag("http.status_code", (int)response.StatusCode);
                activity?.SetTag("http.response_message", response.ReasonPhrase);
            };
        })
        .AddOtlpExporter(o =>
        {
            o.Protocol = OtlpExportProtocol.Grpc;
            o.Endpoint = new Uri("http://localhost:14317");
        })
    )
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter()
    );

builder.Services.AddHttpClient("backendapi", c => { c.BaseAddress = new Uri("http://localhost:5046"); });
builder.Host.ConfigureServices((hostContext, services) => { services.AddHostedService<ConsumerService>(); });
var app = builder.Build();

try
{
    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Program terminated unexpectedly!");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}