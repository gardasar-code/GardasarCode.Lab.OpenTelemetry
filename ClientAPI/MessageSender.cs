using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ClientAPI;

public class MessageSender(IProducer<string, string> producer) : IDisposable
{
    private static readonly ActivitySource ActivitySource = new(nameof(MessageSender));
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public async Task<string> SendMessage()
    {
        const string activityName = "PostToKafka send";
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Producer);

        ActivityContext contextToInject = default;
        if (activity != null)
            contextToInject = activity.Context;
        else if (Activity.Current != null) contextToInject = Activity.Current.Context;

        var headers = new Headers();
        Propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), headers,
            (props, key, value) => { props.Add(key, Encoding.UTF8.GetBytes(value)); });

        var kafkaMessage = new Message<string, string> { Key = "key", Value = "PostToKafka", Headers = headers };
        var result = await producer.ProduceAsync("my-topic", kafkaMessage);

        activity?.AddEvent(new ActivityEvent("MessageSended"));

        return result.Status.ToString();
    }

    public void Dispose()
    {
        producer.Dispose();
    }
}