using System.Numerics;
using AwesomeAssertions;
using Catalog.Domain.Pricing;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-002 (and REQ-QUA-006, since these tests are what kills the mutants of <see cref="Money"/>): properties of money over generated amounts. The oracle for
/// whether an amount is accepted is plain integer arithmetic on the digits, so it shares no code with the decimal arithmetic that the model uses.
/// </summary>
public sealed class MoneyPropertyTests
{
    private const long MaxCents = 99_999_999_999_999L;

    private const long HalfMaxCents = MaxCents / 2;

    private static readonly Gen<long> AnyCents = Gen.Long[0, MaxCents];

    private static readonly Gen<long> SmallCents = Gen.Long[0, 10_000_000_000_000L];

    /// <summary>An amount as a mantissa and a scale, from one to 18 digits long, so that both sides of every bound are generated often.</summary>
    private static readonly Gen<(long Mantissa, int Scale, bool Negative)> AnyAmount =
        Gen.Select(Gen.Long[0, long.MaxValue], Gen.Int[1, 18], Gen.Int[0, 6], Gen.Bool)
            .Select(parts => (Mantissa: parts.Item1 % (long)Math.Pow(10, parts.Item2), Scale: parts.Item3, Negative: parts.Item4));

    private static Money Cents(long cents) => Make.Usd(cents / 100m);

    private static decimal Decimal(long mantissa, int scale, bool negative) =>
        new((int)(mantissa & 0xFFFFFFFF), (int)(mantissa >> 32), 0, negative, (byte)scale);

    private static bool Accepts(long mantissa, int scale, int minorUnits)
    {
        var digitsPastMinorUnits = scale - minorUnits;
        var preciseEnough = digitsPastMinorUnits <= 0 || mantissa % (long)BigInteger.Pow(10, digitsPastMinorUnits) == 0;
        var smallEnough = new BigInteger(mantissa) < BigInteger.Pow(10, Money.MaxIntegerDigits + scale);

        return preciseEnough && smallEnough;
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void An_amount_is_accepted_exactly_when_it_is_not_negative_below_the_limit_and_fits_the_currency()
    {
        Property.Check(
            AnyAmount.Select(Gen.OneOfConst([.. Currency.All])),
            sample =>
            {
                var ((mantissa, scale, negative), currency) = sample;
                var amount = Decimal(mantissa, scale, negative);
                var expected = mantissa == 0 || (!negative && Accepts(mantissa, scale, currency.MinorUnits));

                var result = Money.Create(amount, currency);

                result.IsSuccess.Should().Be(expected, $"{amount} {currency}");
                if (result.IsSuccess)
                {
                    result.Value.Amount.Should().Be(amount);
                    ((int)result.Value.Amount.Scale).Should().Be(currency.MinorUnits);
                }
                else
                {
                    result.Error.Field.Should().Be("amount");
                }
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Addition_is_commutative_and_zero_is_its_identity()
    {
        Property.Check(
            SmallCents.Select(SmallCents),
            sample =>
            {
                var (first, second) = sample;

                Cents(first).Add(Cents(second)).Value.Should().Be(Cents(second).Add(Cents(first)).Value);
                Cents(first).Add(Money.Zero(Currency.Usd)).Value.Should().Be(Cents(first));
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Addition_is_associative_and_adds_the_amounts()
    {
        Property.Check(
            Gen.Select(SmallCents, SmallCents, SmallCents),
            sample =>
            {
                var (a, b, c) = sample;

                var left = Cents(a).Add(Cents(b)).Value.Add(Cents(c)).Value;
                var right = Cents(a).Add(Cents(b).Add(Cents(c)).Value).Value;

                left.Should().Be(right);
                left.Amount.Should().Be((a + b + c) / 100m);
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Subtraction_undoes_addition_and_an_amount_minus_itself_is_zero()
    {
        Property.Check(
            SmallCents.Select(SmallCents),
            sample =>
            {
                var (first, second) = sample;
                var sum = Cents(first).Add(Cents(second)).Value;

                sum.Subtract(Cents(second)).Value.Should().Be(Cents(first));
                Cents(first).Subtract(Cents(first)).Value.Should().Be(Money.Zero(Currency.Usd));
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Subtraction_succeeds_exactly_when_the_result_is_not_negative()
    {
        Property.Check(
            AnyCents.Select(AnyCents),
            sample =>
            {
                var (first, second) = sample;

                var result = Cents(first).Subtract(Cents(second));

                result.IsSuccess.Should().Be(first >= second);
                if (result.IsSuccess)
                {
                    result.Value.Amount.Should().Be((first - second) / 100m);
                }
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Addition_fails_exactly_when_the_sum_reaches_the_limit()
    {
        Property.Check(
            Gen.Long[HalfMaxCents, MaxCents].Select(Gen.Long[HalfMaxCents, MaxCents]),
            sample =>
            {
                var (first, second) = sample;

                var result = Cents(first).Add(Cents(second));

                result.IsSuccess.Should().Be(first + second <= MaxCents);
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Two_spellings_of_one_amount_are_equal_and_hash_alike()
    {
        Property.Check(
            AnyCents.Select(Gen.Int[0, 4]),
            sample =>
            {
                var (cents, extraZeros) = sample;
                var padded = Decimal(cents * (long)Math.Pow(10, extraZeros), 2 + extraZeros, negative: false);

                var plain = Cents(cents);
                var spelled = Money.Create(padded, Currency.Usd).Value;

                spelled.Should().Be(plain);
                spelled.GetHashCode().Should().Be(plain.GetHashCode());
                spelled.ToString().Should().Be(plain.ToString());
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Yen_amounts_keep_no_decimal_places_through_arithmetic()
    {
        Property.Check(
            Gen.Long[0, 400_000_000_000L].Select(Gen.Long[0, 400_000_000_000L]),
            sample =>
            {
                var (first, second) = sample;
                var sum = Make.Jpy(first).Add(Make.Jpy(second)).Value;

                sum.Amount.Scale.Should().Be(0);
                sum.Amount.Should().Be(first + second);
                sum.Subtract(Make.Jpy(second)).Value.Should().Be(Make.Jpy(first));
            });
    }
}
