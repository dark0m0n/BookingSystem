using TicketBooking.Domain.Payment;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class DomainErrorTests
{
    [Fact]
    public void HoldExpired_HasTheSameErrorBeforeAndAfterExpiryIsRecorded()
    {
        var hold = LifecycleTestData.Hold();
        var clock = LifecycleTestData.At(hold.ExpiresAt);

        Assert.Equal(DomainError.HoldExpired,
            Assert.Throws<DomainRuleException>(() => hold.EnsureCanConvert(hold.UserId, clock)).Error);
        hold.Expire(clock);
        Assert.Equal(DomainError.HoldExpired,
            Assert.Throws<DomainRuleException>(() => hold.EnsureCanConvert(hold.UserId, clock)).Error);
    }

    [Fact]
    public void Payment_DistinguishesConflictingFinalOutcomesFromIdempotentReplays()
    {
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), LifecycleTestData.Booking(), "key", LifecycleTestData.Clock);
        payment.Succeed("provider-1");
        payment.Succeed("provider-1");

        var error = Assert.Throws<DomainRuleException>(() => payment.Fail("provider-1"));

        Assert.Equal(DomainError.PaymentAlreadyProcessed, error.Error);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }
}
