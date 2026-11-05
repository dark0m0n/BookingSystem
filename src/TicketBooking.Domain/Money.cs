namespace TicketBooking.Domain;

/// <summary>
/// A nonnegative monetary value with an explicit supported currency and decimal(18,2) precision.
/// Excess fractional precision is rejected rather than silently rounded.
/// </summary>
public sealed record Money
{
    public const int Precision = 18;
    public const int Scale = 2;
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public Money(decimal amount, string currency)
    {
        if (amount < 0 || amount > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must fit nonnegative decimal(18,2).");
        }

        if (decimal.Round(amount, Scale) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must have at most two fractional digits.");
        }

        var code = DomainGuard.Text(currency, nameof(currency)).Trim().ToUpperInvariant();
        if (code is not ("UAH" or "EUR" or "USD"))
        {
            throw new ArgumentException("Supported currencies are UAH, EUR, and USD.", nameof(currency));
        }

        Amount = amount;
        Currency = code;
    }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new ArgumentException("Amounts in different currencies cannot be added.", nameof(other));
        }

        return new Money(checked(Amount + other.Amount), Currency);
    }

    public static Money Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        {
            throw new ArgumentException("At least one value is required to determine the currency.", nameof(values));
        }

        var first = iterator.Current ?? throw new ArgumentException("Values must not contain null.", nameof(values));
        // A one-item total must not alias the item's EF complex value.
        var total = new Money(first.Amount, first.Currency);
        while (iterator.MoveNext())
        {
            total = total.Add(iterator.Current);
        }

        return total;
    }
}
