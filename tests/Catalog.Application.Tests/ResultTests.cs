using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Common;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-004: a use case returns a <see cref="Result{T}"/> whose failure is one of the typed errors, and a rule of the domain becomes the matching typed error.</summary>
public sealed class ResultTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_success_holds_its_value_and_no_error()
    {
        var result = Result.Success("value");

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Value.Should().Be("value");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_failure_holds_its_error_and_refuses_to_give_a_value()
    {
        var error = new NotFound("product");

        var result = Result.Failure<string>(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeSameAs(error);
        result.Invoking(failed => failed.Value).Should().Throw<InvalidOperationException>().WithMessage("*NotFound*");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_result_needs_a_value_or_an_error()
    {
        var withoutValue = () => Result.Success<string>(null!);
        var withoutError = () => Result.Failure<string>(null!);

        withoutValue.Should().Throw<ArgumentNullException>();
        withoutError.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void An_invalid_value_becomes_a_validation_error_on_its_field()
    {
        var error = ApplicationError.From(DomainError.Invalid("price.amount", "The amount must not be negative."));

        error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(new FieldError("price.amount", "The amount must not be negative."));
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_category_that_is_not_active_becomes_a_validation_error_on_the_category()
    {
        var error = ApplicationError.From(new DomainError(DomainErrorCode.CategoryNotActive, "categoryId", "The category is discontinued."));

        error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(new FieldError("categoryId", "The category is discontinued."));
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_discontinued_product_becomes_a_conflict()
    {
        var error = ApplicationError.From(new DomainError(DomainErrorCode.ProductDiscontinued, string.Empty, "The product is discontinued."));

        error.Should().Be(new Conflict(ConflictReason.ProductDiscontinued));
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_domain_error_code_that_the_application_does_not_know_is_a_programming_error()
    {
        var unknown = () => ApplicationError.From(new DomainError((DomainErrorCode)99, "name", "A message."));
        var missing = () => ApplicationError.From(null!);

        unknown.Should().Throw<ArgumentOutOfRangeException>();
        missing.Should().Throw<ArgumentNullException>();
    }
}
