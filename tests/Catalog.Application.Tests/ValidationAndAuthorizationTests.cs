using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Decorators;
using FluentValidation;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-005 and REQ-IDN-002: validators and the authorization check run before the handler, and the handler never runs for a request that either one refuses.</summary>
public sealed class ValidationAndAuthorizationTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task Every_validator_runs_and_all_the_messages_come_back_at_once_in_order()
    {
        var journal = new Journal();
        var handler = new FakeCommandHandler(journal);
        var first = new SampleCommandValidator(journal);
        var second = new SampleCommandValidator(journal, validator => validator.RuleFor(command => command.Name).MinimumLength(5).WithMessage("The name is too short."));
        var decorator = new ValidationCommandDecorator<SampleCommand, SampleView>(handler, [first, second]);

        var result = await decorator.HandleAsync(new SampleCommand(" "), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(
            new FieldError("name", "The name is required."),
            new FieldError("name", "The name is required."),
            new FieldError("name", "The name is too short."));
        handler.Calls.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_valid_command_and_a_command_without_validators_reach_the_handler()
    {
        var journal = new Journal();
        var handler = new FakeCommandHandler(journal);

        var validated = await new ValidationCommandDecorator<SampleCommand, SampleView>(handler, [new SampleCommandValidator(journal)])
            .HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);
        var unvalidated = await new ValidationCommandDecorator<SampleCommand, SampleView>(handler, [])
            .HandleAsync(new SampleCommand(" "), CancellationToken.None);

        validated.IsSuccess.Should().BeTrue();
        unvalidated.IsSuccess.Should().BeTrue();
        handler.Calls.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_query_is_validated_in_the_same_way()
    {
        var journal = new Journal();
        var handler = new FakeQueryHandler<SampleQuery>(journal);
        var decorator = new ValidationQueryDecorator<SampleQuery, SampleView>(handler, [new SampleQueryValidator(journal)]);

        var invalid = await decorator.HandleAsync(new SampleQuery(" "), CancellationToken.None);
        var valid = await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        invalid.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(new FieldError("id", "The identifier is required."));
        valid.IsSuccess.Should().BeTrue();
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("Price.Amount", "price.amount")]
    [InlineData("CategoryId", "categoryId")]
    [InlineData("Items[0].Name", "items[0].name")]
    [InlineData("pageSize", "pageSize")]
    [InlineData("", "")]
    [Trait("Requirement", "REQ-APP-005")]
    public void A_property_name_becomes_the_json_path_of_the_contract(string propertyName, string expected)
    {
        RequestValidation.JsonPath(propertyName).Should().Be(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public void The_validation_helpers_need_a_name()
    {
        var act = () => RequestValidation.JsonPath(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(WriteCases))]
    [Trait("Requirement", "REQ-APP-005")]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_command_runs_only_for_a_caller_with_the_scope_and_one_of_the_roles(string caller, bool allowed)
    {
        var journal = new Journal();
        var handler = new FakeCommandHandler(journal);
        var decorator = new AuthorizationCommandDecorator<SampleCommand, SampleView>(handler, Callers.Named(caller));

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
        handler.Calls.Should().Be(allowed ? 1 : 0);
        (result.Error is Forbidden).Should().Be(!allowed);
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    [Trait("Requirement", "REQ-APP-005")]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_query_runs_only_for_a_caller_with_the_scope_and_one_of_the_roles(string caller, bool allowed)
    {
        var journal = new Journal();
        var handler = new FakeQueryHandler<SampleQuery>(journal);
        var decorator = new AuthorizationQueryDecorator<SampleQuery, SampleView>(handler, Callers.Named(caller));

        var result = await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
        handler.Calls.Should().Be(allowed ? 1 : 0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_caller_with_a_scope_and_no_matching_role_is_told_that_the_role_is_missing()
    {
        var journal = new Journal();
        var decorator = new AuthorizationQueryDecorator<UncachedQuery, SampleView>(new FakeQueryHandler<UncachedQuery>(journal), Callers.Editor);

        var result = await decorator.HandleAsync(new UncachedQuery("1"), CancellationToken.None);

        result.Error.Should().Be(new Forbidden(AccessLevel.Admin, ForbiddenReason.MissingRole));
    }

    public static TheoryData<string, bool> WriteCases => new()
    {
        { "reader", false },
        { "editor", true },
        { "admin", true },
        { "anonymous", false },
        { "write scope without a role", false },
        { "editor role without the scope", false },
    };

    public static TheoryData<string, bool> ReadCases => new()
    {
        { "reader", true },
        { "editor", true },
        { "admin", true },
        { "anonymous", false },
        { "write scope without a role", false },
        { "editor role without the scope", false },
    };
}
