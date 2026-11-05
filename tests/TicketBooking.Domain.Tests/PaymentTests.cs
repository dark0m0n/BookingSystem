using TicketBooking.Domain.Payment;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class PaymentTests
{
    [Fact]
    public void Payment_StartsPendingWithoutAProviderReference()
    {
        var bookingId = Guid.NewGuid();
        var payment = new PaymentEntity(Guid.NewGuid(), bookingId, 12.50m, "request-1");

        Assert.Equal(bookingId, payment.BookingId);
        Assert.Equal(12.50m, payment.Amount);
        Assert.Equal("request-1", payment.IdempotencyKey);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ProviderReference);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Succeeded, true)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Succeeded, PaymentStatus.Succeeded, true)]
    [InlineData(PaymentStatus.Succeeded, PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Succeeded, false)]
    public void Payment_EnforcesResultsAndAllowsIdenticalReplays(PaymentStatus from, PaymentStatus to, bool allowed)
    {
        var payment = Create();
        if (from != PaymentStatus.Pending)
        {
            Complete(payment, from, "provider-1");
        }

        if (allowed)
        {
            Complete(payment, to, "provider-1");
            Assert.Equal(to, payment.Status);
            Assert.Equal("provider-1", payment.ProviderReference);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => Complete(payment, to, "provider-1"));
            Assert.Equal(from, payment.Status);
            Assert.Equal("provider-1", payment.ProviderReference);
        }
    }

    [Fact]
    public void PendingOutcome_CanRecordTheProviderOperationBeforeCompletion()
    {
        var payment = Create();
        payment.RecordProviderReference("provider-1");
        payment.RecordProviderReference("provider-1");

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Throws<InvalidOperationException>(() => payment.RecordProviderReference("provider-2"));
        Assert.Throws<InvalidOperationException>(() => payment.Succeed("provider-2"));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        payment.Succeed("provider-1");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed)]
    public void CompletedPayment_RejectsReferenceChanges(PaymentStatus status)
    {
        var payment = Create();
        Complete(payment, status, "provider-1");

        Assert.Throws<InvalidOperationException>(() => Complete(payment, status, "provider-2"));
        Assert.Throws<InvalidOperationException>(() => payment.RecordProviderReference("provider-1"));
        Assert.Equal("provider-1", payment.ProviderReference);
        Assert.Equal(status, payment.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ProviderOutcome_WhenReferenceIsMissing_DoesNotChangeState(string? reference)
    {
        var payment = Create();

        Assert.ThrowsAny<ArgumentException>(() => payment.Succeed(reference!));
        Assert.ThrowsAny<ArgumentException>(() => payment.Fail(reference!));
        Assert.ThrowsAny<ArgumentException>(() => payment.RecordProviderReference(reference!));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ProviderReference);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Payment_WhenIdempotencyKeyIsMissing_Rejects(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PaymentEntity(Guid.NewGuid(), Guid.NewGuid(), 1m, key!));
    }

    [Fact]
    public void Payment_ValidatesIdentityAndAmount()
    {
        Assert.Throws<ArgumentException>(() => new PaymentEntity(Guid.Empty, Guid.NewGuid(), 1m, "key"));
        Assert.Throws<ArgumentException>(() => new PaymentEntity(Guid.NewGuid(), Guid.Empty, 1m, "key"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaymentEntity(Guid.NewGuid(), Guid.NewGuid(), -0.01m, "key"));
        Assert.Equal(0m, new PaymentEntity(Guid.NewGuid(), Guid.NewGuid(), 0m, "free-request").Amount);
    }

    private static PaymentEntity Create() => new(Guid.NewGuid(), Guid.NewGuid(), 12.50m, "request-1");

    private static void Complete(PaymentEntity payment, PaymentStatus result, string reference)
    {
        if (result == PaymentStatus.Succeeded)
        {
            payment.Succeed(reference);
        }
        else
        {
            payment.Fail(reference);
        }
    }
}
