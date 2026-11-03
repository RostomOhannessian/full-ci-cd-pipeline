using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Credentials;

/// <summary>
/// The source of Kubernetes (plan section 8.4, ADR-0037): it caches the credential that an agent renders into a JSON file, <c>{"username":"...","password":"..."}</c>, and switches to a new one when the file changes.
/// A <see cref="FileSystemWatcher"/> notices the change within milliseconds, and a timer of the clock reads the file again at the poll interval, because a watcher can miss the swap of a projected volume. A file that
/// is missing, half written, or invalid never replaces the credential in use: the source keeps it and logs the kind of the fault. The log text never holds the content of the file or a part of it, and neither does any
/// exception that this class throws. A read at startup that fails stops the host, because the application cannot connect without a credential.
/// </summary>
public sealed partial class RotatingFileCredentialSource : ICredentialSource, IDisposable
{
    // A credential file holds two short strings, so a larger file is not one.
    private const int MaxFileLength = 4096;

    private readonly string _path;
    private readonly ILogger<RotatingFileCredentialSource> _logger;
    private readonly object _gate = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly ITimer _timer;
    private DatabaseCredential _current;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RotatingFileCredentialSource"/> class and reads the file once.</summary>
    /// <param name="path">The path of the credential file.</param>
    /// <param name="pollInterval">How often the clock reads the file again.</param>
    /// <param name="watch">Whether a file watcher also reads the file when it changes. A test turns it off to drive the source with the clock only.</param>
    /// <param name="clock">The clock of the poll timer.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="InvalidOperationException">The file cannot be read at startup. The message names the kind of fault and never the content.</exception>
    public RotatingFileCredentialSource(string path, TimeSpan pollInterval, bool watch, TimeProvider clock, ILogger<RotatingFileCredentialSource> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(clock);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _path = Path.GetFullPath(path);

        var first = TryRead(out var failure);
        _current = first ?? throw new InvalidOperationException($"The credential file cannot be used at startup: {failure}.");

        if (watch && Path.GetDirectoryName(_path) is { Length: > 0 } directory)
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => Refresh();
            _watcher.Created += (_, _) => Refresh();
            _watcher.Renamed += (_, _) => Refresh();
            _watcher.Error += (_, _) => LogWatcherFailed();
            _watcher.EnableRaisingEvents = true;
        }

        _timer = clock.CreateTimer(_ => Refresh(), null, pollInterval, pollInterval);
    }

    /// <inheritdoc />
    public event EventHandler<CredentialRotatedEventArgs>? Rotated;

    /// <inheritdoc />
    public DatabaseCredential Current => Volatile.Read(ref _current);

    /// <summary>Reads the file and switches to the credential in it when it differs from the one in use.</summary>
    /// <returns>True when the source switched to a new credential.</returns>
    public bool Refresh()
    {
        DatabaseCredential previous;
        DatabaseCredential next;

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var read = TryRead(out var failure);
            if (read is null)
            {
                LogRefreshFailed(failure);

                return false;
            }

            if (read == _current)
            {
                return false;
            }

            previous = _current;
            next = read;
            Volatile.Write(ref _current, next);
        }

        LogRotated();
        Raise(new CredentialRotatedEventArgs(previous, next));

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _watcher?.Dispose();
        _timer.Dispose();
    }

    private void Raise(CredentialRotatedEventArgs args)
    {
        var handlers = Rotated;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<CredentialRotatedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
#pragma warning disable CA1031 // A listener must not stop the thread of a timer or a watcher, which would end the process. The fault is logged by its type.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogListenerFailed(exception.GetType().Name);
            }
        }
    }

    // The failures are fixed phrases. The content of the file is never part of one, because a partial read of a secret is still a secret.
    private DatabaseCredential? TryRead(out string failure)
    {
        string text;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            failure = "the file does not exist";

            return null;
        }
        catch (DirectoryNotFoundException)
        {
            failure = "the directory does not exist";

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = "the file cannot be read";

            return null;
        }

        if (text.Length > MaxFileLength)
        {
            failure = "the file is too large";

            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && TryGetText(root, "username", out var userName)
                && TryGetText(root, "password", out var password))
            {
                failure = string.Empty;

                return new DatabaseCredential(userName, password);
            }

            failure = "the file does not hold a username and a password";

            return null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            failure = exception is JsonException ? "the file is not valid JSON" : "the file holds a username or a password of the wrong length";

            return null;
        }
    }

    private static bool TryGetText(JsonElement root, string name, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;

                return true;
            }
        }

        value = string.Empty;

        return false;
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "The database credential changed, and new connections use the new one.")]
    private partial void LogRotated();

    [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "The credential file was not used, and the credential in use stays: {Failure}.")]
    private partial void LogRefreshFailed(string failure);

    [LoggerMessage(EventId = 22, Level = LogLevel.Error, Message = "A listener of the credential rotation failed with {ExceptionType}.")]
    private partial void LogListenerFailed(string exceptionType);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "The watcher of the credential file reported an error, and the poll timer still reads the file.")]
    private partial void LogWatcherFailed();
}
