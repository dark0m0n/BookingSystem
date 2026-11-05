using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class BookingTimingPolicyTests
{
    [Theory]
    [InlineData(0, 15, 60)]
    [InlineData(-1, 15, 60)]
    [InlineData(10, 0, 60)]
    [InlineData(10, -1, 60)]
    [InlineData(10, 15, -1)]
    public void Policy_RejectsInvalidDurations(int holdMinutes, int paymentMinutes, int cancellationMinutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BookingTimingPolicy(
            TimeSpan.FromMinutes(holdMinutes), TimeSpan.FromMinutes(paymentMinutes), TimeSpan.FromMinutes(cancellationMinutes)));
    }

    [Fact]
    public void CustomPolicy_DerivesDeadlinesWithoutChangingExistingObjects()
    {
        var originalHold = LifecycleTestData.Hold();
        var originalBooking = LifecycleTestData.Booking();
        var policy = new BookingTimingPolicy(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(30));
        var sessionId = Guid.NewGuid();
        var hold = new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), LifecycleTestData.SessionStartsAt,
            [LifecycleTestData.Seat(sessionId, 10m)], LifecycleTestData.Clock, policy);
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-TIMING", LifecycleTestData.Clock,
            LifecycleTestData.SessionStartsAt, policy);

        Assert.Equal(LifecycleTestData.Now.AddMinutes(3), hold.ExpiresAt);
        Assert.Equal(LifecycleTestData.Now.AddMinutes(7), booking.PaymentDeadline);
        Assert.Equal(LifecycleTestData.SessionStartsAt.AddMinutes(-30), booking.CancellationDeadline);
        Assert.Equal(LifecycleTestData.Now.AddMinutes(10), originalHold.ExpiresAt);
        Assert.Equal(LifecycleTestData.Now.AddMinutes(15), originalBooking.PaymentDeadline);
        Assert.Equal(LifecycleTestData.Now.AddHours(1), originalBooking.CancellationDeadline);
    }

    [Fact]
    public void Policy_CapsHoldAndPaymentAtSessionStartAndNormalizesOffsets()
    {
        var policy = LifecycleTestData.TimingPolicy;
        var startsAt = LifecycleTestData.Now.AddMinutes(2).ToOffset(TimeSpan.FromHours(2));

        var holdDeadline = policy.GetHoldExpiration(LifecycleTestData.Now, startsAt);
        var paymentDeadline = policy.GetPaymentDeadline(LifecycleTestData.Now, startsAt);
        var cancellationDeadline = policy.GetCancellationDeadline(startsAt);

        Assert.Equal(startsAt, holdDeadline);
        Assert.Equal(startsAt, paymentDeadline);
        Assert.Equal(startsAt.AddHours(-1), cancellationDeadline);
        Assert.Equal(TimeSpan.Zero, holdDeadline.Offset);
        Assert.Equal(TimeSpan.Zero, paymentDeadline.Offset);
        Assert.Equal(TimeSpan.Zero, cancellationDeadline.Offset);
    }

    [Fact]
    public void VeryLargePositiveWindows_AreCappedWithoutDateOverflow()
    {
        var policy = new BookingTimingPolicy(TimeSpan.MaxValue, TimeSpan.MaxValue, TimeSpan.Zero);

        Assert.Equal(LifecycleTestData.SessionStartsAt, policy.GetHoldExpiration(LifecycleTestData.Now, LifecycleTestData.SessionStartsAt));
        Assert.Equal(LifecycleTestData.SessionStartsAt, policy.GetPaymentDeadline(LifecycleTestData.Now, LifecycleTestData.SessionStartsAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Creation_RejectsSessionsThatHaveAlreadyStarted(int minutesUntilStart)
    {
        var policy = LifecycleTestData.TimingPolicy;
        var startsAt = LifecycleTestData.Now.AddMinutes(minutesUntilStart);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetHoldExpiration(LifecycleTestData.Now, startsAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetPaymentDeadline(LifecycleTestData.Now, startsAt));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Confirmation_RequiresCompletionStrictlyBeforeCapturedDeadline(int ticksAfterDeadline, bool allowed)
    {
        var booking = LifecycleTestData.Booking();
        var clock = LifecycleTestData.At(booking.PaymentDeadline.AddTicks(ticksAfterDeadline));

        if (allowed)
        {
            booking.Confirm(clock);
            Assert.Equal(BookingStatus.Confirmed, booking.Status);
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => booking.Confirm(clock));
            Assert.Equal(BookingStatus.PendingPayment, booking.Status);
            booking.Expire(clock);
            Assert.Equal(BookingStatus.Expired, booking.Status);
        }
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void PaymentInitiation_UsesTheSameCapturedCompletionDeadline(int ticksAfterDeadline, bool allowed)
    {
        var booking = LifecycleTestData.Booking();
        var clock = LifecycleTestData.At(booking.PaymentDeadline.AddTicks(ticksAfterDeadline));
        void CreatePayment() => PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "key", clock);

        if (allowed)
        {
            CreatePayment();
        }
        else
        {
            Assert.Throws<DomainRuleException>(CreatePayment);
        }

        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    [Fact]
    public void ZeroCancellationLead_AllowsCancellationUntilSessionStartButNotAtIt()
    {
        var hold = LifecycleTestData.Hold();
        var policy = new BookingTimingPolicy(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.Zero);
        BookingEntity Create() => BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-TIMING",
            LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, policy);
        var before = Create();
        var atStart = Create();

        before.Cancel(before.UserId, LifecycleTestData.At(LifecycleTestData.SessionStartsAt.AddTicks(-1)));
        Assert.Equal(BookingStatus.Cancelled, before.Status);
        Assert.Throws<DomainRuleException>(() => atStart.Cancel(atStart.UserId, LifecycleTestData.At(LifecycleTestData.SessionStartsAt)));
    }

    [Fact]
    public void BookingCreatedInsideCancellationWindow_IsNotCancellable()
    {
        var hold = LifecycleTestData.Hold();
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-TIMING", LifecycleTestData.Clock,
            LifecycleTestData.Now.AddMinutes(30), LifecycleTestData.TimingPolicy);

        Assert.True(booking.CancellationDeadline < booking.CreatedAt);
        Assert.Throws<DomainRuleException>(() => booking.Cancel(booking.UserId, LifecycleTestData.Clock));
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    [Fact]
    public void Policy_IsRequiredByCreationOperations()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<ArgumentNullException>(() => new SeatHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            LifecycleTestData.SessionStartsAt, [], LifecycleTestData.Clock, null!));
        Assert.Throws<ArgumentNullException>(() => BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId,
            "BK-TIMING", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, null!));
    }
}
