namespace TicketBooking.Domain.Payment;

public sealed class Payment
{
    public Payment(Guid id, Guid bookingId, decimal amount, string idempotencyKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        Id = DomainGuard.Id(id, nameof(id));
        BookingId = DomainGuard.Id(bookingId, nameof(bookingId));
        Amount = amount;
        IdempotencyKey = DomainGuard.Text(idempotencyKey, nameof(idempotencyKey));
    }

    public Guid Id { get; }
    public Guid BookingId { get; }
    public string? ProviderReference { get; private set; }
    public decimal Amount { get; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string IdempotencyKey { get; }

    public void RecordProviderReference(string providerReference)
    {
        EnsureReferenceMatches(providerReference);
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending payment can record a provider reference.");
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
            throw new InvalidOperationException("A completed payment cannot change its result.");
        }

        ProviderReference = providerReference;
        Status = result;
    }

    private void EnsureReferenceMatches(string providerReference)
    {
        DomainGuard.Text(providerReference, nameof(providerReference));
        if (ProviderReference is not null && ProviderReference != providerReference)
        {
            throw new InvalidOperationException("The payment already refers to another provider operation.");
        }
    }
}
