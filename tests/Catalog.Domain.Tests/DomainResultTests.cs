using AwesomeAssertions;
using Catalog.Domain.Common;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-002: a failed rule is a value that names the field of the version 1 contract, and a programming error is an exception. The rest of the model
/// builds on these two types, so their behavior is pinned down here.
/// </summary>
public sealed class DomainResultTests
{
    private static readonly DomainError SomeError = new(DomainErrorCode.Invalid, "name", "The name is wrong.");

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_success_without_a_value_has_no_error()
    {
        var result = DomainResult.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_success_with_a_value_returns_it()
    {
        var result = DomainResult.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Value.Should().Be(42);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_failure_without_a_value_carries_its_error()
    {
        var result = DomainResult.Failure(SomeError);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_failure_with_a_value_carries_its_error_and_refuses_to_give_a_value()
    {
        var result = DomainResult.Failure<int>(SomeError);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SomeError);

        var read = () => result.Value;
        read.Should().Throw<InvalidOperationException>().WithMessage("*Invalid*'name'*Check IsSuccess first*");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_failure_requires_an_error()
    {
        var withoutValue = () => DomainResult.Failure(null!);
        var withValue = () => DomainResult.Failure<int>(null!);

        withoutValue.Should().Throw<ArgumentNullException>();
        withValue.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Within_keeps_a_success_and_moves_a_failure_under_its_parent()
    {
        var success = DomainResult.Success("value");
        var failure = DomainResult.Failure<string>(DomainError.Invalid("amount", "Too small."));

        success.Within("price").Should().BeSameAs(success);
        failure.Within("price").Error.Should().Be(new DomainError(DomainErrorCode.Invalid, "price.amount", "Too small."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_field_path_is_nested_once_for_each_level()
    {
        var error = DomainError.Invalid("amount", "Too small.").Within("price").Within("body");

        error.Field.Should().Be("body.price.amount");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void An_error_without_a_field_takes_the_name_of_its_parent()
    {
        var error = new DomainError(DomainErrorCode.ProductDiscontinued, string.Empty, "The product is discontinued.").Within("product");

        error.Field.Should().Be("product");
        error.Code.Should().Be(DomainErrorCode.ProductDiscontinued);
        error.Message.Should().Be("The product is discontinued.");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_parent_path_must_not_be_blank(string parent)
    {
        var nest = () => SomeError.Within(parent);

        nest.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_parent_path_must_not_be_null()
    {
        var nest = () => SomeError.Within(null!);

        nest.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_invalid_error_has_the_invalid_code()
    {
        var error = DomainError.Invalid("sku", "Bad.");

        error.Should().Be(new DomainError(DomainErrorCode.Invalid, "sku", "Bad."));
    }
}
