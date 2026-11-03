using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Catalog.Infrastructure.Credentials;

/// <summary>
/// Builds the connection string of a credential (plan section 8.4, ADR-0037). SqlClient keys a pool by the full connection string, credential included, so the interceptor that opens a connection and the janitor
/// that clears an old pool must build the string with the same function, or the janitor would clear a pool that nobody uses. The builder of SqlClient normalizes the keys, so two strings that differ only in the order
/// or the spelling of the keys give the same result. Pooling is always on, because the requirement is rotation without disabling it.
/// </summary>
public static class CatalogConnectionString
{
    /// <summary>Builds the connection string of a credential from a base string that holds no credential.</summary>
    /// <param name="baseConnectionString">The connection string without a user name or a password.</param>
    /// <param name="credential">The credential.</param>
    public static string Build(string baseConnectionString, DatabaseCredential credential)
    {
        ArgumentNullException.ThrowIfNull(baseConnectionString);
        ArgumentNullException.ThrowIfNull(credential);

        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            UserID = credential.UserName,
            Password = credential.Password,
            Pooling = true,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Checks that a base connection string holds no credential and keeps pooling on, and returns the problems. A problem names the keyword and never repeats a value, because a string that holds a credential by
    /// mistake is exactly the string whose text must not reach a log or an exception.
    /// </summary>
    /// <param name="baseConnectionString">The connection string to check.</param>
    public static IReadOnlyList<string> Validate(string? baseConnectionString)
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return ["ConnectionString is required."];
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(baseConnectionString);
        }
        catch (ArgumentException)
        {
            return ["ConnectionString is not a valid SQL Server connection string."];
        }

        List<string> problems = [];
        if (builder.UserID.Length > 0)
        {
            problems.Add("ConnectionString must not contain a User ID. The credential source supplies it.");
        }

        if (builder.Password.Length > 0)
        {
            problems.Add("ConnectionString must not contain a Password. The credential source supplies it.");
        }

        if (builder.IntegratedSecurity || builder.Authentication != SqlAuthenticationMethod.NotSpecified)
        {
            problems.Add("ConnectionString must use SQL authentication, so it must not set Integrated Security or Authentication.");
        }

        if (!builder.Pooling)
        {
            problems.Add("ConnectionString must keep Pooling on, because rotation works with the pool.");
        }

        return problems;
    }
}

/// <summary>
/// Sets the credential of each new connection before it opens (plan section 8.4, ADR-0037). EF Core opens the connection through the interceptor every time, so a connection that opens after a rotation uses the
/// new credential, and a connection that the pool already holds keeps the login that it was opened with until the pool closes it. The credential comes from the cached source, so the call costs a string lookup.
/// </summary>
public sealed class CredentialConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICredentialSource _source;
    private readonly string _baseConnectionString;
    private (DatabaseCredential Credential, string ConnectionString)? _last;

    /// <summary>Initializes a new instance of the <see cref="CredentialConnectionInterceptor"/> class.</summary>
    /// <param name="source">The source of the credential.</param>
    /// <param name="baseConnectionString">The connection string without a credential.</param>
    public CredentialConnectionInterceptor(ICredentialSource source, string baseConnectionString)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _baseConnectionString = baseConnectionString ?? throw new ArgumentNullException(nameof(baseConnectionString));
    }

    /// <summary>Sets the credential before a synchronous open.</summary>
    /// <param name="connection">The connection that is about to open.</param>
    /// <param name="eventData">The data of the event.</param>
    /// <param name="result">The result that the open would use.</param>
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Apply(connection);

        return result;
    }

    /// <summary>Sets the credential before an asynchronous open.</summary>
    /// <param name="connection">The connection that is about to open.</param>
    /// <param name="eventData">The data of the event.</param>
    /// <param name="result">The result that the open would use.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Apply(connection);

        return ValueTask.FromResult(result);
    }

    private void Apply(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var credential = _source.Current;
        var last = _last;
        if (last is null || !ReferenceEquals(last.Value.Credential, credential))
        {
            last = (credential, CatalogConnectionString.Build(_baseConnectionString, credential));
            _last = last;
        }

        // The string is always set, because SqlClient hides the password in the string that it returns after an open, so a comparison would never match.
        connection.ConnectionString = last.Value.ConnectionString;
    }
}
