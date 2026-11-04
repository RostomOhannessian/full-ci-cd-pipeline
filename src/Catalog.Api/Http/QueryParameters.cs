using System.Globalization;
using Catalog.Application.Results;

namespace Catalog.Api.Http;

/// <summary>
/// Reads the query string of a request, which the contract describes as typed parameters. A value that is not of its type is a validation error that names the parameter, as the contract's <c>400</c> says, and not a
/// failure of the binder that the error handler would have to guess. A parameter that appears twice is an error too, because two values have no meaning for a filter.
/// </summary>
/// <param name="query">The query string of the request.</param>
internal sealed class QueryParameters(IQueryCollection query)
{
    private readonly List<FieldError> _errors = [];

    /// <summary>Gets the problems found so far, in the order that the parameters were read.</summary>
    public IReadOnlyList<FieldError> Errors => _errors;

    /// <summary>Reads a text parameter, or returns null when it is absent.</summary>
    /// <param name="name">The name of the parameter.</param>
    public string? Text(string name)
    {
        if (!query.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count > 1)
        {
            _errors.Add(new FieldError(name, "The parameter must appear at most once."));

            return null;
        }

        return values[0];
    }

    /// <summary>Reads an identifier parameter, or returns null when it is absent or not an identifier.</summary>
    /// <param name="name">The name of the parameter.</param>
    public Guid? Id(string name)
    {
        var text = Text(name);

        if (text is null)
        {
            return null;
        }

        if (Guid.TryParseExact(text, "D", out var id))
        {
            return id;
        }

        _errors.Add(new FieldError(name, "The parameter must be a UUID."));

        return null;
    }

    /// <summary>Reads a whole number, or returns the fallback when the parameter is absent. A value that is not a whole number is an error.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="fallback">The value when the parameter is absent.</param>
    public int Whole(string name, int fallback)
    {
        var text = Text(name);

        if (text is null)
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        _errors.Add(new FieldError(name, "The parameter must be a whole number."));

        return fallback;
    }

    /// <summary>Reads a parameter that names one of a fixed set of values, or returns null when it is absent or not in the set.</summary>
    /// <typeparam name="T">The type of the value that a name stands for.</typeparam>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="allowed">The names that the contract allows and what each stands for.</param>
    public T? Choice<T>(string name, IReadOnlyDictionary<string, T> allowed)
        where T : struct
    {
        var text = Text(name);

        if (text is null)
        {
            return null;
        }

        if (allowed.TryGetValue(text, out var value))
        {
            return value;
        }

        _errors.Add(new FieldError(name, $"The parameter must be one of {string.Join(", ", allowed.Keys)}."));

        return null;
    }
}
