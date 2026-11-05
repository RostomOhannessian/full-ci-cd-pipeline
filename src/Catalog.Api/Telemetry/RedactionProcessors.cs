using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Catalog.Api.Telemetry;

/// <summary>Applies the redactor of a classification to the value of an attribute, and leaves a value that no classification covers as it is.</summary>
/// <param name="redactors">The provider of the redactors, which the composition root registers with <c>AddRedaction</c>.</param>
public sealed class TelemetryRedactor(IRedactorProvider redactors)
{
    private readonly IRedactorProvider _redactors = redactors ?? throw new ArgumentNullException(nameof(redactors));

    /// <summary>Returns the value that may leave the process. It is the same object when nothing was sensitive, so a caller can tell whether the value changed.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The value.</param>
    public object? Apply(string name, object? value)
    {
        var classification = TelemetryClassifier.Classify(name, value);

        return classification == DataClassification.None ? value : _redactors.GetRedactor(classification).Redact(value?.ToString());
    }
}

/// <summary>
/// Redacts the tags of every span before an exporter sees it (plan section 8.11, REQ-OBS-001, REQ-SEC-006, ADR-0043). The processor must come before every exporter in the pipeline, so the composition root adds it first. It
/// runs when the span ends, because the instrumentation sets some tags late, and it rewrites a tag only when the classification of its name or its value says so.
/// </summary>
/// <param name="redactor">The redactor.</param>
public sealed class RedactingActivityProcessor(TelemetryRedactor redactor) : BaseProcessor<Activity>
{
    private readonly TelemetryRedactor _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

    /// <inheritdoc />
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        List<KeyValuePair<string, object?>>? changes = null;

        foreach (var tag in data.TagObjects)
        {
            var redacted = _redactor.Apply(tag.Key, tag.Value);

            if (!ReferenceEquals(redacted, tag.Value))
            {
                (changes ??= []).Add(new KeyValuePair<string, object?>(tag.Key, redacted));
            }
        }

        if (changes is not null)
        {
            foreach (var change in changes)
            {
                data.SetTag(change.Key, change.Value);
            }
        }
    }
}

/// <summary>
/// Redacts every log record before an exporter sees it (plan section 8.11, REQ-OBS-003, REQ-SEC-006, ADR-0043). The attributes of a record are the structured values of its message template, so each one goes through the
/// classifier. The text of an exception never leaves the process, because it can hold a value of a request or a connection string: the record keeps the type of the exception and its stack frames, which hold no message,
/// and the exception itself is removed. The formatted message and the body are checked by the shape of their text. The processor must come before every exporter, so the composition root adds it first.
/// </summary>
/// <param name="redactor">The redactor.</param>
public sealed class RedactingLogProcessor(TelemetryRedactor redactor) : BaseProcessor<LogRecord>
{
    private readonly TelemetryRedactor _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        List<KeyValuePair<string, object?>>? attributes = null;

        if (data.Attributes is { } original)
        {
            for (var index = 0; index < original.Count; index++)
            {
                var attribute = original[index];
                var redacted = _redactor.Apply(attribute.Key, attribute.Value);

                if (!ReferenceEquals(redacted, attribute.Value))
                {
                    attributes ??= [.. original];
                    attributes[index] = new KeyValuePair<string, object?>(attribute.Key, redacted);
                }
            }
        }

        if (data.Exception is { } exception)
        {
            attributes ??= [.. data.Attributes ?? []];
            attributes.Add(new KeyValuePair<string, object?>("exception.type", exception.GetType().FullName));
            attributes.Add(new KeyValuePair<string, object?>("exception.stacktrace", FramesOnly(exception.StackTrace)));
            data.Exception = null;
        }

        if (attributes is not null)
        {
            data.Attributes = attributes;
        }

        if (data.FormattedMessage is { } formatted && !ReferenceEquals(_redactor.Apply("message", formatted), formatted))
        {
            data.FormattedMessage = MarkerRedactor.Marker;
        }

        if (data.Body is { } body && !ReferenceEquals(_redactor.Apply("body", body), body))
        {
            data.Body = MarkerRedactor.Marker;
        }
    }

    // A frame names a method, and a build with symbols adds the path of the file and the line. The path can name a folder of a person, so it is cut, and the frame keeps the name of the method. A plain regular expression
    // and not a generated one, because a generated type would sit in the assembly under a namespace of the base class library (see TelemetryClassifier).
    private static readonly Regex SourceLocation = new(@" in [^\r\n]+?:line \d+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    private static string? FramesOnly(string? stackTrace) =>
        stackTrace is null ? null : SourceLocation.Replace(stackTrace, string.Empty);
}
