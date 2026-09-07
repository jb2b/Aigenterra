using System.Diagnostics;
using System.Net;
using System.Text;
using Aigenterra.Collector;

namespace Aigenterra.Codex.Tests;

/// <summary>
/// Verifies byte-preserving and fail-open Collector behavior.
/// </summary>
public sealed class CollectorCommandTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 4, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Collector_forwards_exact_bytes_and_observation_time()
    {
        using var activities = new CollectorActivityCapture();
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var payload = "{ \"exact\" : true }\r\n"u8.ToArray();
        await using var input = new MemoryStream(payload);
        using var output = new StringWriter();

        var exitCode = await CollectorCommand.ExecuteAsync(
            input,
            output,
            client,
            new Uri("http://127.0.0.1/ingest/codex/hooks"),
            ObservedAt);

        Assert.Equal(0, exitCode);
        Assert.Equal(payload, handler.Body);
        Assert.Equal("2026-09-04T12:34:56.0000000+00:00", handler.ObservedAt);
        Assert.Equal($"{{}}{Environment.NewLine}", output.ToString());
        Assert.Equal("forwarded", activities.Single.GetTagItem("aigenterra.collector.result"));
        Assert.Equal((long)payload.Length, activities.Single.GetTagItem("aigenterra.collector.payload.size"));
        Assert.Equal(200, activities.Single.GetTagItem("http.response.status_code"));
        Assert.DoesNotContain(
            activities.Single.TagObjects,
            tag => string.Equals(tag.Value?.ToString(), Encoding.UTF8.GetString(payload), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collector_fails_open_when_delivery_throws()
    {
        using var activities = new CollectorActivityCapture();
        using var client = new HttpClient(new ThrowingHandler());
        await using var input = new MemoryStream("{}"u8.ToArray());
        using var output = new StringWriter();

        var exitCode = await CollectorCommand.ExecuteAsync(
            input,
            output,
            client,
            new Uri("http://127.0.0.1/ingest/codex/hooks"),
            ObservedAt);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{{}}{Environment.NewLine}", output.ToString());
        Assert.Equal(ActivityStatusCode.Error, activities.Single.Status);
        Assert.Equal("transport_error", activities.Single.GetTagItem("aigenterra.collector.result"));
        Assert.Equal(typeof(HttpRequestException).FullName, activities.Single.GetTagItem("error.type"));
    }

    [Fact]
    public async Task Oversized_payload_is_not_sent_and_still_fails_open()
    {
        using var activities = new CollectorActivityCapture();
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        await using var input = new MemoryStream(new byte[CollectorCommand.MaxPayloadBytes + 1]);
        using var output = new StringWriter();

        var exitCode = await CollectorCommand.ExecuteAsync(
            input,
            output,
            client,
            new Uri("http://127.0.0.1/ingest/codex/hooks"),
            ObservedAt);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal($"{{}}{Environment.NewLine}", output.ToString());
        Assert.Equal(ActivityStatusCode.Error, activities.Single.Status);
        Assert.Equal("payload_too_large", activities.Single.GetTagItem("aigenterra.collector.result"));
        Assert.True((long)activities.Single.GetTagItem("aigenterra.collector.payload.size")! > CollectorCommand.MaxPayloadBytes);
    }

    private sealed class CollectorActivityCapture : IDisposable
    {
        private readonly List<Activity> stoppedActivities = [];
        private readonly ActivityListener listener;

        public CollectorActivityCapture()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Aigenterra.Collector",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = stoppedActivities.Add
            };
            ActivitySource.AddActivityListener(listener);
        }

        public Activity Single => Assert.Single(stoppedActivities);

        public void Dispose() => listener.Dispose();
    }

    /// <summary>
    /// Captures outgoing Collector requests for transport assertions.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public byte[]? Body { get; private set; }

        public string? ObservedAt { get; private set; }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            ObservedAt = request.Headers.GetValues("X-Aigenterra-Observed-At").Single();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Simulates an unavailable ingest endpoint.
    /// </summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Synthetic transport failure.");
    }
}
