using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Tests;

public class SeatHoldTests
{
    [Fact]
    public void Hold_WhenConstructed_CopiesItsItemsAndStartsActive()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var seat = LifecycleTestData.Seat(sessionId, 12.50m);
        var items = new List<SessionSeat> { seat };
        var hold = new SeatHold(id, sessionId, Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(10), items, LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);
        items.Clear();

        Assert.Equal(SeatHoldStatus.Active, hold.Status);
        Assert.Equal(LifecycleTestData.Price(12.50m), Assert.Single(hold.Items).PriceSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<SeatHoldItem>)hold.Items).Clear());
    }

    [Fact]
    public void Convert_WhenOwnerActsBeforeExpiry_ConvertsHold()
    {
        var hold = LifecycleTestData.Hold();

        hold.Convert(hold.UserId, LifecycleTestData.At(hold.ExpiresAt.AddTicks(-1)));

        Assert.Equal(SeatHoldStatus.Converted, hold.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Convert_WhenExpiryIsReached_RejectsWithoutChangingState(int ticksAfterExpiry)
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<DomainRuleException>(() =>
            hold.Convert(hold.UserId, LifecycleTestData.At(hold.ExpiresAt.AddTicks(ticksAfterExpiry))));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Fact]
    public void OwnerOperations_WhenAnotherUserActs_RejectWithoutChangingState()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<DomainRuleException>(() => hold.Convert(Guid.NewGuid(), LifecycleTestData.Clock));
        Assert.Throws<DomainRuleException>(() => hold.Cancel(Guid.NewGuid(), LifecycleTestData.Clock));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Expire_WhenDeadlineIsReached_ExpiresHold(int ticksAfterExpiry)
    {
        var hold = LifecycleTestData.Hold();

        hold.Expire(LifecycleTestData.At(hold.ExpiresAt.AddTicks(ticksAfterExpiry)));

        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
    }

    [Fact]
    public void Expire_BeforeDeadline_RejectsWithoutChangingState()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<DomainRuleException>(() => hold.Expire(LifecycleTestData.At(hold.ExpiresAt.AddTicks(-1))));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Theory]
    [InlineData(SeatHoldStatus.Converted)]
    [InlineData(SeatHoldStatus.Cancelled)]
    [InlineData(SeatHoldStatus.Expired)]
    public void TerminalHold_RejectsOtherTransitionsAndAllowsMatchingCleanupReplay(SeatHoldStatus terminal)
    {
        var hold = LifecycleTestData.Hold();
        switch (terminal)
        {
            case SeatHoldStatus.Converted:
                hold.Convert(hold.UserId, LifecycleTestData.Clock);
                break;
            case SeatHoldStatus.Cancelled:
                hold.Cancel(hold.UserId, LifecycleTestData.Clock);
                break;
            case SeatHoldStatus.Expired:
                hold.Expire(LifecycleTestData.At(hold.ExpiresAt));
                break;
        }

        var expectedError = terminal == SeatHoldStatus.Expired ? DomainError.HoldExpired : DomainError.HoldNotActive;
        Assert.Equal(expectedError,
            Assert.Throws<DomainRuleException>(() => hold.Convert(hold.UserId, LifecycleTestData.Clock)).Error);
        if (terminal == SeatHoldStatus.Cancelled) hold.Cancel(hold.UserId, LifecycleTestData.Clock);
        else Assert.Equal(expectedError,
            Assert.Throws<DomainRuleException>(() => hold.Cancel(hold.UserId, LifecycleTestData.Clock)).Error);
        if (terminal == SeatHoldStatus.Expired) hold.Expire(LifecycleTestData.At(hold.ExpiresAt));
        else Assert.Equal(expectedError,
            Assert.Throws<DomainRuleException>(() => hold.Expire(LifecycleTestData.At(hold.ExpiresAt))).Error);
        Assert.Equal(terminal, hold.Status);
    }

    [Fact]
    public void Cancel_WhenDeadlineIsReached_RequiresExpirationInstead()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<DomainRuleException>(() => hold.Cancel(hold.UserId, LifecycleTestData.At(hold.ExpiresAt)));
        hold.Expire(LifecycleTestData.At(hold.ExpiresAt));
        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Cancel_EnforcesExpiryWithoutChangingPricesOrItems(int ticksFromExpiry)
    {
        var hold = LifecycleTestData.Hold(0.01m, 19.99m);
        var prices = hold.Items.Select(item => item.PriceSnapshot).ToArray();
        var deadline = hold.ExpiresAt;
        var clock = LifecycleTestData.At(deadline.AddTicks(ticksFromExpiry));

        if (ticksFromExpiry < 0)
        {
            hold.Cancel(hold.UserId, clock);
            Assert.Equal(SeatHoldStatus.Cancelled, hold.Status);
        }
        else
        {
            Assert.Equal(DomainError.HoldExpired,
                Assert.Throws<DomainRuleException>(() => hold.Cancel(hold.UserId, clock)).Error);
            Assert.Equal(SeatHoldStatus.Active, hold.Status);
        }

        Assert.Equal(deadline, hold.ExpiresAt);
        Assert.Equal(new Money(20m, "UAH"), hold.TotalAmount);
        Assert.Equal(prices, hold.Items.Select(item => item.PriceSnapshot));
    }

    [Fact]
    public void Hold_WhenSeatsAreEmptyForeignNullOrDuplicated_Rejects()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var seat = LifecycleTestData.Seat(sessionId, 1m);
        var foreign = LifecycleTestData.Seat(Guid.NewGuid(), 1m);

        SeatHold Create(IEnumerable<SessionSeat> seats) => new(id, sessionId, Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(10), seats, LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);

        Assert.Throws<ArgumentException>(() => Create([]));
        Assert.Throws<ArgumentException>(() => Create([foreign]));
        Assert.Throws<ArgumentException>(() => Create([null!]));
        Assert.Throws<ArgumentException>(() => Create([seat, seat]));
        Assert.Throws<ArgumentNullException>(() => Create(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Hold_WhenExpirationIsNotFuture_Rejects(int minutes)
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeatHold(id, Guid.NewGuid(), Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(minutes), [], LifecycleTestData.Clock, LifecycleTestData.TimingPolicy));
    }

    [Fact]
    public void HoldItem_WhenSnapshotIsMissingOrIdentityIsEmpty_Rejects()
    {
        Assert.Throws<ArgumentNullException>(() => new SeatHoldItem(Guid.NewGuid(), Guid.NewGuid(), null!));
        Assert.Throws<ArgumentException>(() => new SeatHoldItem(Guid.Empty, Guid.NewGuid(), LifecycleTestData.Price(1m)));
        Assert.Throws<ArgumentException>(() => new SeatHoldItem(Guid.NewGuid(), Guid.Empty, LifecycleTestData.Price(1m)));
    }
}
