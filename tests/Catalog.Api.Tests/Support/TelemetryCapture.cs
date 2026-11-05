using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Catalog.Api.Tests.Support;

/// <summary>A collection that several threads add to, which the in-memory exporters need, because a span ends on the thread that finishes the work.</summary>
/// <typeparam name="T">The type of the items.</typeparam>
internal sealed class ConcurrentCollection<T> : ICollection<T>
{
    private readonly ConcurrentQueue<T> _items = new();

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(T item) => _items.Enqueue(item);

    public void Clear() => _items.Clear();

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public bool Remove(T item) => throw new NotSupportedException();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>One log record as an exporter of the real host saw it, with its attributes in a dictionary, so a test and a snapshot read plain values.</summary>
/// <param name="Category">The category of the logger.</param>
/// <param name="Level">The severity.</param>
/// <param name="EventId">The event identifier, or 0.</param>
/// <param name="Body">The body, which is the message template.</param>
/// <param name="FormattedMessage">The formatted message, which the host does not export, so it is null.</param>
/// <param name="Attributes">The attributes, which hold the values of the template.</param>
/// <param name="TraceId">The trace that the record belongs to, or null.</param>
/// <param name="HasException">Whether the record still holds an exception object, which the redaction removes.</param>
internal sealed record LoggedRecord(string? Category, LogLevel Level, int EventId, string? Body, string? FormattedMessage, IReadOnlyDictionary<string, string?> Attributes, string? TraceId, bool HasException);

/// <summary>One series of a metric: its tags and the numbers that the exporter received.</summary>
/// <param name="Tags">The tags of the series.</param>
/// <param name="Sum">The sum of a counter, the last value of a gauge, or the sum of a histogram.</param>
/// <param name="Count">The count of a histogram, or 0.</param>
internal sealed record Series(IReadOnlyDictionary<string, string?> Tags, double Sum, long Count);

/// <summary>
/// The in-memory exporters of the three signals (plan section 10.1, REQ-OBS-001, REQ-OBS-002, REQ-OBS-003). A test attaches it to the real host, which adds it after its own redaction, so what the capture holds is what an
/// exporter of the real host receives. A listener of the SDK sees every source and every meter of the process, and test classes run in parallel, so a test finds its spans by the trace that it started (a request with a
/// <c>traceparent</c> header), and it reads metrics only for what holds for every host.
/// </summary>
internal sealed class TelemetryCapture
{
    private readonly ConcurrentCollection<Activity> _spans = new();
    private readonly ConcurrentCollection<MetricSnapshot> _metrics = new();
    private readonly ConcurrentCollection<LogRecord> _logs = new();

    /// <summary>Attaches the exporters to the services of the host. It must run after the composition root registered the telemetry.</summary>
    /// <param name="services">The services of the host.</param>
    public void AddTo(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddInMemoryExporter(_spans))
            .WithMetrics(metrics => metrics.AddInMemoryExporter(_metrics, options => options.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = int.MaxValue))
            .WithLogging(logging => logging.AddInMemoryExporter(_logs));
    }

    /// <summary>Gets every span that ended, in the order that they ended.</summary>
    public IReadOnlyList<Activity> Spans => [.. _spans];

    /// <summary>Gets the spans of one trace.</summary>
    /// <param name="traceId">The trace identifier, as 32 hexadecimal characters.</param>
    public IReadOnlyList<Activity> SpansOf(string traceId) => [.. _spans.Where(span => span.TraceId.ToHexString() == traceId)];

    /// <summary>Gets every log record that the exporter received, as plain values.</summary>
    public IReadOnlyList<LoggedRecord> Logs => [.. _logs.Select(Convert)];

    /// <summary>Collects the metrics now and returns the series of each metric, by the name of the metric. The last collection of a name wins, because the temporality is cumulative.</summary>
    /// <param name="services">The services of the host.</param>
    public IReadOnlyDictionary<string, IReadOnlyList<Series>> FlushMetrics(IServiceProvider services)
    {
        _metrics.Clear();
        services.GetRequiredService<MeterProvider>().ForceFlush(10_000);

        return _metrics
            .GroupBy(metric => metric.Name)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Series>)[.. group.Last().MetricPoints.Select(point => Convert(group.Last().MetricType, point))]);
    }

    /// <summary>Writes every log record, span, and metric series as one text, so a test can search the whole output for a value that must never be in it.</summary>
    /// <param name="services">The services of the host.</param>
    public string Everything(IServiceProvider services)
    {
        var text = new System.Text.StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        foreach (var record in Logs)
        {
            text.AppendLine(JsonSerializer.Serialize(record));
        }

        foreach (var span in Spans)
        {
            text.AppendLine(culture, $"{span.OperationName} {span.DisplayName} {span.StatusDescription}");

            foreach (var tag in span.TagObjects)
            {
                text.AppendLine(culture, $"{tag.Key}={tag.Value}");
            }

            foreach (var spanEvent in span.Events)
            {
                text.AppendLine(spanEvent.Name);

                foreach (var tag in spanEvent.Tags)
                {
                    text.AppendLine(culture, $"{tag.Key}={tag.Value}");
                }
            }
        }

        foreach (var (name, series) in FlushMetrics(services))
        {
            foreach (var item in series)
            {
                text.AppendLine(culture, $"{name} {string.Join(',', item.Tags.Select(tag => $"{tag.Key}={tag.Value}"))}");
            }
        }

        return text.ToString();
    }

    private static LoggedRecord Convert(LogRecord record) =>
        new(
            record.CategoryName,
            record.LogLevel,
            record.EventId.Id,
            record.Body,
            record.FormattedMessage,
            (record.Attributes ?? []).ToDictionary(attribute => attribute.Key, attribute => attribute.Value?.ToString()),
            record.TraceId == default ? null : record.TraceId.ToHexString(),
            record.Exception is not null);

    private static Series Convert(MetricType type, MetricPoint point)
    {
        Dictionary<string, string?> tags = [];

        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }

        return type switch
        {
            MetricType.LongSum or MetricType.LongSumNonMonotonic => new Series(tags, point.GetSumLong(), 0),
            MetricType.DoubleSum or MetricType.DoubleSumNonMonotonic => new Series(tags, point.GetSumDouble(), 0),
            MetricType.LongGauge => new Series(tags, point.GetGaugeLastValueLong(), 0),
            MetricType.DoubleGauge => new Series(tags, point.GetGaugeLastValueDouble(), 0),
            MetricType.Histogram => new Series(tags, point.GetHistogramSum(), point.GetHistogramCount()),
            _ => new Series(tags, 0, 0),
        };
    }
}
