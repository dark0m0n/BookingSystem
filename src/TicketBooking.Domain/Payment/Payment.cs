using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Payment;

public sealed class Payment
{
    // Used by persistence; application callers must use the validated creation API.
    private Payment()
    {
    }

    private Payment(Guid id, Guid bookingId, Money amount, string idempotencyKey)
    {
        Id = DomainGuard.Id(id, nameof(id));
        BookingId = DomainGuard.Id(bookingId, nameof(bookingId));
        Amount = new Money(amount.Amount, amount.Currency);
        IdempotencyKey = DomainGuard.Text(idempotencyKey, nameof(idempotencyKey));
    }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public string? ProviderReference { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>
    /// Derives the charge from a pending booking's server-calculated total; no caller amount is accepted.
    /// </summary>
    public static Payment CreateForBooking(Guid id, BookingEntity booking, string idempotencyKey, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(timeProvider);
        booking.EnsurePaymentCanCompleteAt(timeProvider.GetUtcNow().ToUniversalTime());

        return new Payment(id, booking.Id, booking.TotalAmount, idempotencyKey);
    }

    public void RecordProviderReference(string providerReference)
    {
        EnsureReferenceMatches(providerReference);
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainRuleException(DomainError.PaymentAlreadyProcessed, "Only a pending payment can record a provider reference.");
        }

        ProviderReference = providerReference;
    }

    public void Succeed(string providerReference)
    {
        Complete(PaymentStatus.Succeeded, providerReference);
    }

    public void Fail(string providerReference)
    {
        Complete(PaymentStatus.Failed, providerReference);
    }

    private void Complete(PaymentStatus result, string providerReference)
    {
        EnsureReferenceMatches(providerReference);
        if (Status == result)
        {
            // Repeating the same result for the same provider operation has no additional effect.
            return;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new DomainRuleException(DomainError.PaymentAlreadyProcessed, "A completed payment cannot change its result.");
        }

        ProviderReference = providerReference;
        Status = result;
    }

    private void EnsureReferenceMatches(string providerReference)
    {
        DomainGuard.Text(providerReference, nameof(providerReference));
        if (ProviderReference is not null && ProviderReference != providerReference)
        {
            throw new DomainRuleException(DomainError.PaymentReferenceMismatch, "The payment already refers to another provider operation.");
        }
    }
}
