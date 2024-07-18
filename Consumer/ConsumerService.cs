using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Serilog;

namespace Consumer;

public class ConsumerService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("ConsumerService");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    private readonly IConsumer<string, string> _consumer;
    private readonly IHttpClientFactory _httpClientFactory;

    public ConsumerService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        var config = new ConsumerConfig
        {
            GroupId = "consumer-group",
            BootstrapServers = "localhost:9092",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe("my-topic");

        // var listener = new ActivityListener
        // {
        //     ShouldListenTo = (source) => source.Name == "ConsumerService",
        //     Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
        //     SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllDataAndRecorded
        // };
        //ActivitySource.AddActivityListener(listener);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cr = _consumer.Consume(stoppingToken);

            var parentContext = Propagator.Extract(default, cr.Message.Headers, (headers, key) =>
            {
                var header = headers.FirstOrDefault(h => h.Key == key);
                var value = header is null ? string.Empty : Encoding.UTF8.GetString(header.GetValueBytes());
                return header is null
                    ? Enumerable.Empty<string>()
                    : new List<string> { value };
            });

            Baggage.Current = parentContext.Baggage;

            using var activity =
                ActivitySource.StartActivity("GetFromKafka", ActivityKind.Consumer, parentContext.ActivityContext);

            activity?.SetTag("message", cr.Message.Value);
            activity?.SetTag("test_message", cr.Message.Value);
            activity?.AddEvent(new ActivityEvent("MessageConsumed"));

            try
            {
                var client = _httpClientFactory.CreateClient("backendapi");
                var response = await client.GetStringAsync("backend");
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Error while reading from backendapi");
            }
        }
    }
}