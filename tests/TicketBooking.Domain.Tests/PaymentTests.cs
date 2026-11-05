using TicketBooking.Domain.Payment;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class PaymentTests
{
    [Fact]
    public void Payment_StartsPendingWithoutAProviderReference()
    {
        var booking = LifecycleTestData.Booking(12.50m);
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "request-1", LifecycleTestData.Clock);

        Assert.Equal(booking.Id, payment.BookingId);
        Assert.Equal(LifecycleTestData.Price(12.50m), payment.Amount);
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
            Assert.Equal(DomainError.PaymentAlreadyProcessed,
                Assert.Throws<DomainRuleException>(() => Complete(payment, to, "provider-1")).Error);
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
        Assert.Throws<DomainRuleException>(() => payment.RecordProviderReference("provider-2"));
        Assert.Throws<DomainRuleException>(() => payment.Succeed("provider-2"));
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

        Assert.Equal(DomainError.PaymentReferenceMismatch,
            Assert.Throws<DomainRuleException>(() => Complete(payment, status, "provider-2")).Error);
        Assert.Equal(DomainError.PaymentAlreadyProcessed,
            Assert.Throws<DomainRuleException>(() => payment.RecordProviderReference("provider-1")).Error);
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
        Assert.ThrowsAny<ArgumentException>(() => PaymentEntity.CreateForBooking(Guid.NewGuid(), LifecycleTestData.Booking(), key!, LifecycleTestData.Clock));
    }

    [Fact]
    public void Payment_ValidatesIdentityAndBooking()
    {
        var booking = LifecycleTestData.Booking(0m);
        Assert.Throws<ArgumentException>(() => PaymentEntity.CreateForBooking(Guid.Empty, booking, "key", LifecycleTestData.Clock));
        Assert.Throws<ArgumentNullException>(() => PaymentEntity.CreateForBooking(Guid.NewGuid(), null!, "key", LifecycleTestData.Clock));
        Assert.Equal(LifecycleTestData.Price(0m), PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "free-request", LifecycleTestData.Clock).Amount);
    }

    private static PaymentEntity Create() =>
        PaymentEntity.CreateForBooking(Guid.NewGuid(), LifecycleTestData.Booking(12.50m), "request-1", LifecycleTestData.Clock);

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
