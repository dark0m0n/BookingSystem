using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Tests;

public class BookingTests
{
    [Fact]
    public void CreateFromHold_CopiesHistoricalPricesAndCalculatesTotal()
    {
        var hold = LifecycleTestData.Hold(125.55m, 35.45m, 0m);
        var id = Guid.NewGuid();

        var booking = BookingEntity.CreateFromHold(id, hold, hold.UserId, "BK-001", LifecycleTestData.Clock);

        Assert.Equal(id, booking.Id);
        Assert.Equal(hold.UserId, booking.UserId);
        Assert.Equal(hold.SessionId, booking.SessionId);
        Assert.Equal("BK-001", booking.Number);
        Assert.Equal(LifecycleTestData.Now, booking.CreatedAt);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(161m, booking.TotalAmount);
        Assert.All(booking.Items, item => Assert.Equal(id, item.BookingId));
        Assert.Equal(hold.Items.Select(item => item.SessionSeatId), booking.Items.Select(item => item.SessionSeatId));
        Assert.Equal(hold.Items.Select(item => item.PriceSnapshot), booking.Items.Select(item => item.PriceSnapshot));
        Assert.Throws<NotSupportedException>(() => ((IList<BookingItem>)booking.Items).Clear());
        // Application, not Booking, owns committing the hold conversion.
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Fact]
    public void CreateFromHold_WhenOwnerOrExpiryIsInvalid_Rejects()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<InvalidOperationException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, Guid.NewGuid(), "BK-001", LifecycleTestData.Clock));
        Assert.Throws<InvalidOperationException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", LifecycleTestData.At(hold.ExpiresAt)));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Theory]
    [InlineData(SeatHoldStatus.Converted)]
    [InlineData(SeatHoldStatus.Cancelled)]
    [InlineData(SeatHoldStatus.Expired)]
    public void CreateFromHold_WhenHoldIsTerminal_Rejects(SeatHoldStatus status)
    {
        var hold = LifecycleTestData.Hold();
        if (status == SeatHoldStatus.Converted)
        {
            hold.Convert(hold.UserId, LifecycleTestData.Clock);
        }
        else if (status == SeatHoldStatus.Cancelled)
        {
            hold.Cancel(hold.UserId, LifecycleTestData.Clock);
        }
        else
        {
            hold.Expire(LifecycleTestData.At(hold.ExpiresAt));
        }

        Assert.Throws<InvalidOperationException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", LifecycleTestData.Clock));
    }

    [Fact]
    public void CreateFromHold_WhenBookingDataOrTotalIsInvalid_DoesNotConsumeHold()
    {
        var hold = LifecycleTestData.Hold();
        Assert.Throws<ArgumentException>(() =>
            BookingEntity.CreateFromHold(Guid.Empty, hold, hold.UserId, "BK-001", LifecycleTestData.Clock));
        Assert.Throws<ArgumentException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, " ", LifecycleTestData.Clock));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);

        var overflowingHold = LifecycleTestData.Hold(decimal.MaxValue, 1m);
        Assert.Throws<OverflowException>(() => BookingEntity.CreateFromHold(Guid.NewGuid(), overflowingHold,
            overflowingHold.UserId, "BK-002", LifecycleTestData.Clock));
        Assert.Equal(SeatHoldStatus.Active, overflowingHold.Status);
    }

    public static IEnumerable<object[]> Transitions()
    {
        var states = new[] { BookingStatus.PendingPayment, BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Expired };
        var targets = new[] { BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Expired };
        foreach (var from in states)
        {
            foreach (var to in targets)
            {
                var allowed = from == BookingStatus.PendingPayment ||
                    (from == BookingStatus.Confirmed && to == BookingStatus.Cancelled);
                yield return new object[] { from, to, allowed };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Booking_EnforcesTheTransitionTable(BookingStatus from, BookingStatus to, bool allowed)
    {
        var booking = LifecycleTestData.Booking();
        if (from != BookingStatus.PendingPayment)
        {
            Transition(booking, from);
        }

        if (allowed)
        {
            Transition(booking, to);
            Assert.Equal(to, booking.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => Transition(booking, to));
            Assert.Equal(from, booking.Status);
        }
    }

    [Fact]
    public void Cancel_RequiresTheOwnerAndDoesNotChangeTotal()
    {
        var booking = LifecycleTestData.Booking();
        booking.Confirm();

        Assert.Throws<InvalidOperationException>(() => booking.Cancel(Guid.NewGuid(), LifecycleTestData.Clock,
            LifecycleTestData.Now.AddHours(2), LifecycleTestData.Now.AddHours(1)));
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        booking.Cancel(booking.UserId, LifecycleTestData.Clock,
            LifecycleTestData.Now.AddHours(2), LifecycleTestData.Now.AddHours(1));
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(100m, booking.TotalAmount);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Cancel_EnforcesTheExactDeadline(int ticksFromDeadline, bool allowed)
    {
        var booking = LifecycleTestData.Booking();
        var deadline = LifecycleTestData.Now.AddHours(1);
        void Cancel() => booking.Cancel(booking.UserId, LifecycleTestData.At(deadline.AddTicks(ticksFromDeadline)), deadline.AddHours(1), deadline);

        if (allowed)
        {
            Cancel();
            Assert.Equal(BookingStatus.Cancelled, booking.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(Cancel);
            Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        }
    }

    [Fact]
    public void Cancel_WhenDeadlineIsAfterSessionStart_Rejects()
    {
        var booking = LifecycleTestData.Booking();

        Assert.Throws<ArgumentOutOfRangeException>(() => booking.Cancel(booking.UserId, LifecycleTestData.Clock,
            LifecycleTestData.Now.AddHours(1), LifecycleTestData.Now.AddHours(2)));
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
    }

    [Fact]
    public void Expire_RequiresAValidReachedPaymentDeadline()
    {
        var booking = LifecycleTestData.Booking();
        var deadline = LifecycleTestData.Now.AddMinutes(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => booking.Expire(LifecycleTestData.At(deadline), booking.CreatedAt));
        Assert.Throws<InvalidOperationException>(() => booking.Expire(LifecycleTestData.At(deadline.AddTicks(-1)), deadline));
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        booking.Expire(LifecycleTestData.At(deadline), deadline);
        Assert.Equal(BookingStatus.Expired, booking.Status);
    }

    [Fact]
    public void BookingItem_ValidatesIdentityAndSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new BookingItem(Guid.Empty, Guid.NewGuid(), 0m));
        Assert.Throws<ArgumentException>(() => new BookingItem(Guid.NewGuid(), Guid.Empty, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BookingItem(Guid.NewGuid(), Guid.NewGuid(), -1m));
    }

    private static void Transition(BookingEntity booking, BookingStatus target)
    {
        switch (target)
        {
            case BookingStatus.Confirmed:
                booking.Confirm();
                break;
            case BookingStatus.Cancelled:
                booking.Cancel(booking.UserId, LifecycleTestData.Clock,
                    LifecycleTestData.Now.AddHours(2), LifecycleTestData.Now.AddHours(1));
                break;
            case BookingStatus.Expired:
                booking.Expire(LifecycleTestData.At(LifecycleTestData.Now.AddMinutes(10)), LifecycleTestData.Now.AddMinutes(10));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
