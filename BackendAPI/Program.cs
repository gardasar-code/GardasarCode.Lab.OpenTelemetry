using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oracle.ManagedDataAccess.OpenTelemetry;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().Enrich.FromLogContext()
    .Enrich.WithCorrelationId().WriteTo.Console(new RenderedCompactJsonFormatter()).CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, BackendAPI.AppJsonSerializerContext.Default);
});

builder.Logging.AddOpenTelemetry(options =>
{
    options
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService("ClientAPI"));
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("BackendAPI"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options => { options.RecordException = true; })
        .AddOracleDataProviderInstrumentation(options => { options.RecordException = true; })
        .AddNpgsql()
        .AddOtlpExporter(o =>
        {
            o.Protocol = OtlpExportProtocol.Grpc;
            o.Endpoint = new Uri("http://localhost:14317");
        })
    )
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter()
    );

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

var backendApi = app.MapGroup("/backend");
backendApi.MapGet("/", async () =>
{
    var users = new List<string>();

    await using var connection =
        new NpgsqlConnection("Host=localhost;Username=user;Password=password;Database=db");
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("select agginitval from pg_catalog.pg_aggregate", connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
        if (!await reader.IsDBNullAsync(0))
            users.Add(reader.GetString(0));

    return Results.Ok(users);
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