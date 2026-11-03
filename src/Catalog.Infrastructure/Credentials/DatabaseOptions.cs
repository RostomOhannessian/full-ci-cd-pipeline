using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Credentials;

/// <summary>How the application gets the credential of its database login (plan section 8.4, ADR-0037).</summary>
public enum CredentialMode
{
    /// <summary>One credential from the configuration, for the Compose stack. It never rotates.</summary>
    Static,

    /// <summary>A credential that an agent renders into a file and rotates, for Kubernetes.</summary>
    RotatingFile,
}

/// <summary>
/// The settings of the database connection and of its credential (plan section 8.4, ADR-0037). The connection string holds no credential, and the source supplies it. The type prints nothing but its name,
/// because a settings object that holds a password must not show it in a log or a debugger display by accident.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Database";

    /// <summary>Gets or sets the connection string without a user name or a password.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets where the credential comes from. Default: <see cref="CredentialMode.Static"/>.</summary>
    public CredentialMode Mode { get; set; } = CredentialMode.Static;

    /// <summary>Gets or sets the user name of the static credential.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password of the static credential. It comes from an environment variable or a secret, never from a file in the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the path of the credential file that the rotating mode reads.</summary>
    public string CredentialFile { get; set; } = string.Empty;

    /// <summary>Gets or sets how often the rotating mode reads the file again, as a fallback for a missed file event. Default 30 seconds.</summary>
    public TimeSpan CredentialPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets whether a file watcher also reads the file when it changes. Default true.</summary>
    public bool WatchCredentialFile { get; set; } = true;

    /// <summary>Gets or sets how long after a rotation the pool of the old credential is cleared. Default 60 seconds.</summary>
    public TimeSpan PoolClearGrace { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Checks the settings and returns the problems. A problem names a setting and never repeats a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [.. CatalogConnectionString.Validate(ConnectionString)];

        if (Mode == CredentialMode.Static && (string.IsNullOrEmpty(UserName) || string.IsNullOrEmpty(Password)))
        {
            problems.Add("UserName and Password are required in the static mode.");
        }

        if (Mode == CredentialMode.RotatingFile)
        {
            if (string.IsNullOrWhiteSpace(CredentialFile))
            {
                problems.Add("CredentialFile is required in the rotating file mode.");
            }

            if (CredentialPollInterval <= TimeSpan.Zero)
            {
                problems.Add("CredentialPollInterval must be positive.");
            }
        }

        if (PoolClearGrace < TimeSpan.Zero || PoolClearGrace > TimeSpan.FromHours(1))
        {
            problems.Add("PoolClearGrace must be between zero and one hour.");
        }

        return problems;
    }

    /// <inheritdoc />
    public override string ToString() => nameof(DatabaseOptions);
}

/// <summary>Chooses and builds the source of the credential for a set of settings.</summary>
public static class CredentialSources
{
    /// <summary>Builds the source that the settings name.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    /// <param name="clock">The clock of the poll timer of the rotating source.</param>
    /// <param name="loggers">The factory of the loggers.</param>
    public static ICredentialSource Create(DatabaseOptions options, TimeProvider clock, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);

        return options.Mode switch
        {
            CredentialMode.Static => new StaticCredentialSource(new DatabaseCredential(options.UserName, options.Password)),
            CredentialMode.RotatingFile => new RotatingFileCredentialSource(
                options.CredentialFile,
                options.CredentialPollInterval,
                options.WatchCredentialFile,
                clock,
                loggers.CreateLogger<RotatingFileCredentialSource>()),
            _ => throw new ArgumentOutOfRangeException(nameof(options), "The credential mode is not known."),
        };
    }
}

/// <summary>Validates <see cref="DatabaseOptions"/> when the host starts, so a wrong setting stops the host before it serves a request.</summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var problems = options?.Validate() ?? ["The database settings are missing."];

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>Validates <see cref="OutboxOptions"/> when the host starts.</summary>
public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        var problems = options?.Validate() ?? ["The outbox settings are missing."];

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>Validates <see cref="IdempotencyOptions"/> when the host starts.</summary>
public sealed class IdempotencyOptionsValidator : IValidateOptions<IdempotencyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        var problems = options?.Validate() ?? ["The idempotency settings are missing."];

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
