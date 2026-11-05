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

        var booking = BookingEntity.CreateFromHold(id, hold, hold.UserId, "BK-001", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);

        Assert.Equal(id, booking.Id);
        Assert.Equal(hold.UserId, booking.UserId);
        Assert.Equal(hold.SessionId, booking.SessionId);
        Assert.Equal("BK-001", booking.Number);
        Assert.Equal(LifecycleTestData.Now, booking.CreatedAt);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(LifecycleTestData.Price(161m), booking.TotalAmount);
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

        Assert.Throws<DomainRuleException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, Guid.NewGuid(), "BK-001", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
        Assert.Throws<DomainRuleException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", LifecycleTestData.At(hold.ExpiresAt), LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
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

        Assert.Throws<DomainRuleException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
    }

    [Fact]
    public void CreateFromHold_WhenBookingDataIsInvalid_DoesNotConsumeHold()
    {
        var hold = LifecycleTestData.Hold();
        Assert.Throws<ArgumentException>(() =>
            BookingEntity.CreateFromHold(Guid.Empty, hold, hold.UserId, "BK-001", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
        Assert.Throws<ArgumentException>(() =>
            BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, " ", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);

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
            var expectedError = to == BookingStatus.Cancelled
                ? DomainError.BookingNotCancellable
                : DomainError.BookingNotPendingPayment;
            Assert.Equal(expectedError, Assert.Throws<DomainRuleException>(() => Transition(booking, to)).Error);
            Assert.Equal(from, booking.Status);
        }
    }

    [Fact]
    public void Cancel_RequiresTheOwnerAndDoesNotChangeTotal()
    {
        var booking = LifecycleTestData.Booking();
        booking.Confirm(LifecycleTestData.Clock);

        Assert.Throws<DomainRuleException>(() => booking.Cancel(Guid.NewGuid(), LifecycleTestData.Clock));
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        booking.Cancel(booking.UserId, LifecycleTestData.Clock);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(LifecycleTestData.Price(100m), booking.TotalAmount);
    }

    [Theory]
    [InlineData(BookingStatus.PendingPayment, -1, true)]
    [InlineData(BookingStatus.PendingPayment, 0, false)]
    [InlineData(BookingStatus.PendingPayment, 1, false)]
    [InlineData(BookingStatus.Confirmed, -1, true)]
    [InlineData(BookingStatus.Confirmed, 0, false)]
    [InlineData(BookingStatus.Confirmed, 1, false)]
    public void Cancel_EnforcesTheExactDeadline(BookingStatus initialStatus, int ticksFromDeadline, bool allowed)
    {
        var booking = LifecycleTestData.Booking();
        if (initialStatus == BookingStatus.Confirmed)
        {
            booking.Confirm(LifecycleTestData.Clock);
        }
        var deadline = booking.CancellationDeadline;
        void Cancel() => booking.Cancel(booking.UserId, LifecycleTestData.At(deadline.AddTicks(ticksFromDeadline)));

        if (allowed)
        {
            Cancel();
            Assert.Equal(BookingStatus.Cancelled, booking.Status);
        }
        else
        {
            Assert.Equal(DomainError.CancellationDeadlinePassed, Assert.Throws<DomainRuleException>(Cancel).Error);
            Assert.Equal(initialStatus, booking.Status);
        }

        Assert.Equal(deadline, booking.CancellationDeadline);
        Assert.Equal(new Money(100m, "UAH"), booking.TotalAmount);
    }

    [Fact]
    public void Booking_CapturesDeadlinesFromTheTimingPolicy()
    {
        var booking = LifecycleTestData.Booking();

        Assert.Equal(LifecycleTestData.Now.AddMinutes(15), booking.PaymentDeadline);
        Assert.Equal(LifecycleTestData.Now.AddHours(1), booking.CancellationDeadline);
        Assert.Equal(LifecycleTestData.SessionStartsAt, booking.SessionStartsAt);
    }

    [Fact]
    public void Expire_RequiresAValidReachedPaymentDeadline()
    {
        var booking = LifecycleTestData.Booking();
        var deadline = booking.PaymentDeadline;

        Assert.Throws<DomainRuleException>(() => booking.Expire(LifecycleTestData.At(deadline.AddTicks(-1))));
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        booking.Expire(LifecycleTestData.At(deadline));
        Assert.Equal(BookingStatus.Expired, booking.Status);
    }

    [Fact]
    public void BookingItem_ValidatesIdentityAndSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new BookingItem(Guid.Empty, Guid.NewGuid(), LifecycleTestData.Price(0m)));
        Assert.Throws<ArgumentException>(() => new BookingItem(Guid.NewGuid(), Guid.Empty, LifecycleTestData.Price(0m)));
        Assert.Throws<ArgumentNullException>(() => new BookingItem(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    private static void Transition(BookingEntity booking, BookingStatus target)
    {
        switch (target)
        {
            case BookingStatus.Confirmed:
                booking.Confirm(LifecycleTestData.Clock);
                break;
            case BookingStatus.Cancelled:
                booking.Cancel(booking.UserId, LifecycleTestData.Clock);
                break;
            case BookingStatus.Expired:
                booking.Expire(LifecycleTestData.At(booking.PaymentDeadline));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
