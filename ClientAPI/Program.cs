using System.Diagnostics;
using ClientAPI;
using Confluent.Kafka;
using OpenTelemetry.Context.Propagation;
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
                .AddService("ClientAPI"))
        ;
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ClientAPI"))
    .WithTracing(tracing => tracing
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
        .AddSource(nameof(MessageSender))
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
    )
    ;

builder.Services.AddSingleton(new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = "kafka:9092"
}).Build());

builder.Services.AddSingleton<MessageSender>();
builder.Services.AddHttpClient("backendapi", c => { c.BaseAddress = new Uri("http://localhost:5046"); });

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

var clientApi = app.MapGroup("/client");
clientApi.MapGet("/", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("backendapi");
    var response = await client.GetStringAsync("backend");
    return Results.Ok("Response from BackendAPI — " + response);
});

ActivitySource activitySource = new("ApiService");
var propagator = Propagators.DefaultTextMapPropagator;

var kafkaApi = app.MapGroup("/kafka");
kafkaApi.MapGet("/", async (MessageSender sender) =>
{
    var result = await sender.SendMessage();
    return Results.Ok(result);
});

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