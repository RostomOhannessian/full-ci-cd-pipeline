using System.Globalization;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Caching;

/// <summary>
/// The settings of the connection to Valkey, which serves as the shared cache and as the backplane of the catalog cache (plan sections 8.1 and 8.5, ADR-0038). The user is an access control list user that reaches only the keys and
/// the channel of its environment, and the password comes from an environment variable or a secret and never from a file in the repository. The type prints nothing but its name, because a settings object that holds a
/// password must not show it in a log or a debugger display by accident. The password does not rotate in this work package: the broker that issues rotating Valkey credentials arrives with the platform (Phase 3).
/// </summary>
public sealed class ValkeyOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Valkey";

    /// <summary>The largest length of a user name or a password.</summary>
    public const int MaxCredentialLength = 256;

    /// <summary>Gets or sets the address of the server as <c>host:port</c>. Default: <c>localhost:6379</c>.</summary>
    public string Endpoint { get; set; } = "localhost:6379";

    /// <summary>Gets or sets the name of the access control list user.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password of the user. It comes from an environment variable or a secret, never from a file in the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the connection uses TLS. The Compose stack on one machine uses none, and a platform that crosses a network must. Default: false.</summary>
    public bool UseTls { get; set; }

    /// <summary>Gets or sets how long a connection waits to be established. Default: 1 second.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how long a command waits for its reply. It must be longer than the timeout of the cache, which gives up first. Default: 1 second.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the name that the connection gives itself, which an operator sees in the client list of the server. Default: <c>catalog-api</c>.</summary>
    public string ClientName { get; set; } = "catalog-api";

    /// <summary>Checks the settings and returns the problems. A problem names a setting and never repeats a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (!IsEndpoint(Endpoint))
        {
            problems.Add("Endpoint must be a host name or an address and a port, written as host:port, with a port from 1 to 65535.");
        }

        if (string.IsNullOrEmpty(UserName) || UserName.Length > MaxCredentialLength)
        {
            problems.Add($"UserName is required and holds at most {MaxCredentialLength} characters. The default user is off in the access control list, so a connection needs its own user.");
        }

        if (string.IsNullOrEmpty(Password) || Password.Length > MaxCredentialLength)
        {
            problems.Add($"Password is required and holds at most {MaxCredentialLength} characters.");
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            problems.Add("ConnectTimeout must be positive.");
        }

        if (CommandTimeout <= TimeSpan.Zero)
        {
            problems.Add("CommandTimeout must be positive.");
        }

        if (string.IsNullOrWhiteSpace(ClientName) || ClientName.Any(char.IsWhiteSpace))
        {
            problems.Add("ClientName is required and holds no white space.");
        }

        return problems;
    }

    /// <inheritdoc />
    public override string ToString() => nameof(ValkeyOptions);

    private static bool IsEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Any(character => char.IsWhiteSpace(character) || character is ',' or '='))
        {
            return false;
        }

        var separator = endpoint.LastIndexOf(':');

        return separator > 0
            && int.TryParse(endpoint.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535;
    }
}

/// <summary>Validates <see cref="ValkeyOptions"/> when the host starts, so a wrong setting stops the host before it serves a request.</summary>
public sealed class ValkeyOptionsValidator : IValidateOptions<ValkeyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ValkeyOptions options)
    {
        var problems = options?.Validate() ?? ["The Valkey settings are missing."];

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
