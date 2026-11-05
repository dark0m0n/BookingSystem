using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Payment;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class DeadlineInteractionTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void HoldCappedAtSessionStart_ConversionAndExpiryMeetAtTheSameInstant(int ticksFromStart)
    {
        var startsAt = LifecycleTestData.Now.AddMinutes(2);
        var hold = CreateHold(startsAt);
        var clock = LifecycleTestData.At(startsAt.AddTicks(ticksFromStart));

        Assert.Equal(startsAt, hold.ExpiresAt);
        if (ticksFromStart < 0)
        {
            Assert.Equal(DomainError.HoldNotExpired,
                Assert.Throws<DomainRuleException>(() => hold.Expire(clock)).Error);
            Assert.Equal(SeatHoldStatus.Active, hold.Status);
            hold.Convert(hold.UserId, clock);
            Assert.Equal(SeatHoldStatus.Converted, hold.Status);
        }
        else
        {
            Assert.Equal(DomainError.HoldExpired,
                Assert.Throws<DomainRuleException>(() => hold.Convert(hold.UserId, clock)).Error);
            Assert.Equal(SeatHoldStatus.Active, hold.Status);
            hold.Expire(clock);
            Assert.Equal(SeatHoldStatus.Expired, hold.Status);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void PaymentCappedAtSessionStart_InitiationConfirmationAndExpiryShareTheCutoff(int ticksFromStart)
    {
        var startsAt = LifecycleTestData.Now.AddMinutes(2);
        var hold = CreateHold(startsAt);
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-CAPPED",
            LifecycleTestData.Clock, startsAt, LifecycleTestData.TimingPolicy);
        var clock = LifecycleTestData.At(startsAt.AddTicks(ticksFromStart));

        Assert.Equal(startsAt, booking.PaymentDeadline);
        if (ticksFromStart < 0)
        {
            Assert.Equal(DomainError.PaymentDeadlineNotReached,
                Assert.Throws<DomainRuleException>(() => booking.Expire(clock)).Error);
            Assert.Equal(BookingStatus.PendingPayment, booking.Status);
            var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "before-start", clock);
            Assert.Equal(booking.TotalAmount, payment.Amount);
            booking.Confirm(clock);
            Assert.Equal(BookingStatus.Confirmed, booking.Status);
        }
        else
        {
            Assert.Equal(DomainError.PaymentDeadlinePassed,
                Assert.Throws<DomainRuleException>(() => PaymentEntity.CreateForBooking(
                    Guid.NewGuid(), booking, "at-or-after-start", clock)).Error);
            Assert.Equal(DomainError.PaymentDeadlinePassed,
                Assert.Throws<DomainRuleException>(() => booking.Confirm(clock)).Error);
            Assert.Equal(BookingStatus.PendingPayment, booking.Status);
            booking.Expire(clock);
            Assert.Equal(BookingStatus.Expired, booking.Status);
        }

        Assert.Equal(startsAt, booking.PaymentDeadline);
        Assert.Equal(new Money(100m, "UAH"), booking.TotalAmount);
    }

    [Fact]
    public void ConversionJustBeforeHoldExpiry_StartsTheBookingWindowAtConversion()
    {
        var hold = LifecycleTestData.Hold();
        var originalExpiry = hold.ExpiresAt;
        var createdAt = originalExpiry.AddTicks(-1);
        var clock = LifecycleTestData.At(createdAt);
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-CONVERSION",
            clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
        hold.Convert(hold.UserId, clock);
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(createdAt, booking.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(15), booking.PaymentDeadline);
        Assert.Equal(originalExpiry, hold.ExpiresAt);
        Assert.Equal(DomainError.HoldNotActive, Assert.Throws<DomainRuleException>(() => hold.Expire(clock)).Error);
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "after-original-hold-expiry", clock);
        Assert.Equal(booking.TotalAmount, payment.Amount);
        Assert.Equal(SeatHoldStatus.Converted, hold.Status);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    [Fact]
    public void PaymentAttempts_DoNotExtendTheCapturedDeadline()
    {
        var booking = LifecycleTestData.Booking(0.01m, 19.99m);
        var deadline = booking.PaymentDeadline;
        var first = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "first-attempt", LifecycleTestData.Clock);
        first.Fail("provider-first");
        var clock = LifecycleTestData.At(deadline.AddTicks(-1));
        var retry = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "second-attempt", clock);
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(DomainError.PaymentDeadlinePassed,
            Assert.Throws<DomainRuleException>(() => PaymentEntity.CreateForBooking(
                Guid.NewGuid(), booking, "too-late", clock)).Error);
        Assert.Equal(deadline, booking.PaymentDeadline);
        Assert.Equal(new Money(20m, "UAH"), first.Amount);
        Assert.Equal(first.Amount, retry.Amount);
        Assert.Equal(PaymentStatus.Failed, first.Status);
        Assert.Equal(PaymentStatus.Pending, retry.Status);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    [Fact]
    public void LateSuccessfulPayment_CannotConfirmAnExpiredBooking()
    {
        var booking = LifecycleTestData.Booking();
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "pending-attempt", LifecycleTestData.Clock);
        var clock = LifecycleTestData.At(booking.PaymentDeadline);
        booking.Expire(clock);
        payment.Succeed("late-provider-result");

        Assert.Equal(DomainError.BookingNotPendingPayment,
            Assert.Throws<DomainRuleException>(() => booking.Confirm(clock)).Error);
        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(booking.TotalAmount, payment.Amount);
    }

    private static SeatHold CreateHold(DateTimeOffset startsAt)
    {
        var sessionId = Guid.NewGuid();
        return new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), startsAt,
            [LifecycleTestData.Seat(sessionId, 100m)], LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);
    }
}
