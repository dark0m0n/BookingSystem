using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Tests;

public class UtcTimeTests
{
    [Theory]
    [InlineData(-300)]
    [InlineData(120)]
    [InlineData(330)]
    public void Session_NormalizesOffsetInputsToUtcWithoutChangingInstants(int offsetMinutes)
    {
        var start = LifecycleTestData.Now.ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        var end = start.AddHours(2);

        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);

        Assert.Equal(TimeSpan.Zero, session.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, session.EndsAt.Offset);
        Assert.Equal(start, session.StartsAt);
        Assert.Equal(end, session.EndsAt);
    }

    [Fact]
    public void Hold_UsesTheInjectedClockAcrossTheExactUtcExpiryBoundary()
    {
        var clock = LifecycleTestData.Clock;
        var id = Guid.NewGuid();
        var deadline = LifecycleTestData.Now.AddMinutes(10).ToOffset(TimeSpan.FromHours(2));
        var sessionId = Guid.NewGuid();
        var hold = new SeatHold(id, sessionId, Guid.NewGuid(), deadline, [LifecycleTestData.Seat(sessionId, 10m)], clock, LifecycleTestData.TimingPolicy);

        Assert.Equal(TimeSpan.Zero, hold.ExpiresAt.Offset);
        Assert.Equal(deadline, hold.ExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(10).Subtract(TimeSpan.FromTicks(1)));
        hold.EnsureCanConvert(hold.UserId, clock);
        Assert.Throws<DomainRuleException>(() => hold.Expire(clock));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Throws<DomainRuleException>(() => hold.Convert(hold.UserId, clock));
        Assert.Throws<DomainRuleException>(() => hold.Cancel(hold.UserId, clock));
        hold.Expire(clock);
        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
    }

    [Fact]
    public void Booking_CreationUsesOneClockReadingForValidationAndCreatedAt()
    {
        var hold = LifecycleTestData.Hold();
        var instant = hold.ExpiresAt.AddTicks(-1);
        var clock = new AdvancingTimeProvider(instant);

        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-UTC", clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);

        Assert.Equal(1, clock.Reads);
        Assert.Equal(instant, booking.CreatedAt);
        Assert.Equal(TimeSpan.Zero, booking.CreatedAt.Offset);
    }

    [Fact]
    public void Booking_ExpiresAtTheSameInstantForANonUtcDeadline()
    {
        var clock = LifecycleTestData.Clock;
        var hold = LifecycleTestData.Hold();
        clock.Advance(TimeSpan.FromMinutes(1));
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-UTC", clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
        var deadline = booking.PaymentDeadline.ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal(LifecycleTestData.Now.AddMinutes(1), booking.CreatedAt);
        Assert.Equal(TimeSpan.Zero, booking.CreatedAt.Offset);
        clock.Advance((deadline - clock.GetUtcNow()).Subtract(TimeSpan.FromTicks(1)));
        Assert.Throws<DomainRuleException>(() => booking.Expire(clock));
        clock.Advance(TimeSpan.FromTicks(1));
        booking.Expire(clock);
        Assert.Equal(BookingStatus.Expired, booking.Status);
    }

    [Fact]
    public void Booking_CancellationUsesTheClockAndComparesMixedOffsetsByInstant()
    {
        var before = LifecycleTestData.Booking();
        var atDeadline = LifecycleTestData.Booking();
        var clock = LifecycleTestData.Clock;
        var sessionStart = LifecycleTestData.Now.AddHours(2).ToOffset(TimeSpan.FromHours(2));
        var deadline = LifecycleTestData.Now.AddHours(1).ToOffset(TimeSpan.FromHours(-5));
        clock.Advance(TimeSpan.FromHours(1).Subtract(TimeSpan.FromTicks(1)));

        before.Cancel(before.UserId, clock);
        Assert.Equal(BookingStatus.Cancelled, before.Status);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Throws<DomainRuleException>(() => atDeadline.Cancel(atDeadline.UserId, clock));
        Assert.Equal(BookingStatus.PendingPayment, atDeadline.Status);
    }

    [Fact]
    public void TimeSensitiveOperations_RejectAMissingClock()
    {
        var hold = LifecycleTestData.Hold();
        var booking = LifecycleTestData.Booking();
        var deadline = LifecycleTestData.Now.AddHours(1);

        Assert.Throws<ArgumentNullException>(() => new SeatHold(hold.Id, hold.SessionId, hold.UserId, hold.ExpiresAt, [], null!, LifecycleTestData.TimingPolicy));
        Assert.Throws<ArgumentNullException>(() => hold.EnsureCanConvert(hold.UserId, null!));
        Assert.Throws<ArgumentNullException>(() => hold.Convert(hold.UserId, null!));
        Assert.Throws<ArgumentNullException>(() => hold.Cancel(hold.UserId, null!));
        Assert.Throws<ArgumentNullException>(() => hold.Expire(null!));
        Assert.Throws<ArgumentNullException>(() => BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-UTC", null!, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
        Assert.Throws<ArgumentNullException>(() => booking.Cancel(booking.UserId, null!));
        Assert.Throws<ArgumentNullException>(() => booking.Expire(null!));
        Assert.Throws<ArgumentNullException>(() => booking.Confirm(null!));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow() => initialTime.AddTicks(Reads++);
    }
}
