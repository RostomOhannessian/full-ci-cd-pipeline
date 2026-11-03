namespace Catalog.Infrastructure.Credentials;

/// <summary>
/// The user name and the password of one database login (plan section 8.4, ADR-0037). The value never prints its password: <see cref="ToString"/> shows the user name and a marker, so a log line, an exception
/// message, or a debugger display of an object that holds a credential cannot leak it. Two credentials are equal when both parts are equal, which is how the pool janitor tells the credential in use from an old one.
/// </summary>
public sealed record DatabaseCredential
{
    /// <summary>The largest length of a user name or a password. SQL Server allows 128 characters in a login name, and a generated password is far shorter than this limit.</summary>
    public const int MaxLength = 256;

    /// <summary>Initializes a new instance of the <see cref="DatabaseCredential"/> class.</summary>
    /// <param name="userName">The name of the login.</param>
    /// <param name="password">The password of the login.</param>
    /// <exception cref="ArgumentException">A part is empty or longer than <see cref="MaxLength"/>. The message names the part and never its value.</exception>
    public DatabaseCredential(string userName, string password)
    {
        UserName = Check(userName, nameof(userName));
        Password = Check(password, nameof(password));
    }

    /// <summary>Gets the name of the login.</summary>
    public string UserName { get; }

    /// <summary>Gets the password of the login. Never write it to a log, a message, or a metric.</summary>
    public string Password { get; }

    /// <inheritdoc />
    public override string ToString() => $"DatabaseCredential {{ UserName = {UserName}, Password = [redacted] }}";

    private static string Check(string value, string name) =>
        value is { Length: > 0 and <= MaxLength }
            ? value
            : throw new ArgumentException($"The {name} must have between 1 and {MaxLength} characters.", name);
}

/// <summary>Tells a listener that a credential source switched to a new credential.</summary>
public sealed class CredentialRotatedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="CredentialRotatedEventArgs"/> class.</summary>
    /// <param name="previous">The credential that was in use until now. Its pool must be cleared after a grace period.</param>
    /// <param name="current">The credential that every new connection uses from now on.</param>
    public CredentialRotatedEventArgs(DatabaseCredential previous, DatabaseCredential current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>Gets the credential that was in use until now.</summary>
    public DatabaseCredential Previous { get; }

    /// <summary>Gets the credential that every new connection uses from now on.</summary>
    public DatabaseCredential Current { get; }
}

/// <summary>
/// The source of the database credential (plan section 8.4, ADR-0037). The value is cached in memory, so a read costs nothing, and a connection asks for it each time it opens. A source that can rotate raises
/// <see cref="Rotated"/> after it switched, and the pool janitor clears the pool of the old credential after a grace period.
/// </summary>
public interface ICredentialSource
{
    /// <summary>Occurs after the source switched to a new credential, on the thread that noticed the change.</summary>
    event EventHandler<CredentialRotatedEventArgs>? Rotated;

    /// <summary>Gets the credential that a new connection uses now.</summary>
    DatabaseCredential Current { get; }
}

/// <summary>
/// The source of the Compose stack: one credential from the configuration, which never changes (the password of the local database comes from <c>.env</c>). It never raises <see cref="ICredentialSource.Rotated"/>.
/// </summary>
/// <param name="credential">The credential.</param>
public sealed class StaticCredentialSource(DatabaseCredential credential) : ICredentialSource
{
    /// <inheritdoc />
    public event EventHandler<CredentialRotatedEventArgs>? Rotated
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public DatabaseCredential Current { get; } = credential ?? throw new ArgumentNullException(nameof(credential));
}
