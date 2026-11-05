using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Tests;

public class SeatHoldTests
{
    [Fact]
    public void Hold_WhenConstructed_CopiesItsItemsAndStartsActive()
    {
        var id = Guid.NewGuid();
        var item = new SeatHoldItem(id, Guid.NewGuid(), 12.50m);
        var items = new List<SeatHoldItem> { item };
        var hold = new SeatHold(id, Guid.NewGuid(), Guid.NewGuid(),
            LifecycleTestData.Now.AddMinutes(10), items, LifecycleTestData.Clock);
        items.Clear();

        Assert.Equal(SeatHoldStatus.Active, hold.Status);
        Assert.Equal(12.50m, Assert.Single(hold.Items).PriceSnapshot);
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

        Assert.Throws<InvalidOperationException>(() =>
            hold.Convert(hold.UserId, LifecycleTestData.At(hold.ExpiresAt.AddTicks(ticksAfterExpiry))));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Fact]
    public void OwnerOperations_WhenAnotherUserActs_RejectWithoutChangingState()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<InvalidOperationException>(() => hold.Convert(Guid.NewGuid(), LifecycleTestData.Clock));
        Assert.Throws<InvalidOperationException>(() => hold.Cancel(Guid.NewGuid(), LifecycleTestData.Clock));
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

        Assert.Throws<InvalidOperationException>(() => hold.Expire(LifecycleTestData.At(hold.ExpiresAt.AddTicks(-1))));
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
    }

    [Theory]
    [InlineData(SeatHoldStatus.Converted)]
    [InlineData(SeatHoldStatus.Cancelled)]
    [InlineData(SeatHoldStatus.Expired)]
    public void TerminalHold_RejectsEveryFurtherTransition(SeatHoldStatus terminal)
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

        Assert.Throws<InvalidOperationException>(() => hold.Convert(hold.UserId, LifecycleTestData.Clock));
        Assert.Throws<InvalidOperationException>(() => hold.Cancel(hold.UserId, LifecycleTestData.Clock));
        Assert.Throws<InvalidOperationException>(() => hold.Expire(LifecycleTestData.At(hold.ExpiresAt)));
        Assert.Equal(terminal, hold.Status);
    }

    [Fact]
    public void Cancel_WhenDeadlineIsReached_RequiresExpirationInstead()
    {
        var hold = LifecycleTestData.Hold();

        Assert.Throws<InvalidOperationException>(() => hold.Cancel(hold.UserId, LifecycleTestData.At(hold.ExpiresAt)));
        hold.Expire(LifecycleTestData.At(hold.ExpiresAt));
        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
    }

    [Fact]
    public void Hold_WhenItemsAreEmptyForeignNullOrDuplicated_Rejects()
    {
        var id = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var item = new SeatHoldItem(id, seatId, 1m);
        var foreign = new SeatHoldItem(Guid.NewGuid(), seatId, 1m);

        SeatHold Create(IEnumerable<SeatHoldItem> items) => new(id, Guid.NewGuid(), Guid.NewGuid(),
            LifecycleTestData.Now.AddMinutes(10), items, LifecycleTestData.Clock);

        Assert.Throws<ArgumentException>(() => Create([]));
        Assert.Throws<ArgumentException>(() => Create([foreign]));
        Assert.Throws<ArgumentException>(() => Create([null!]));
        Assert.Throws<ArgumentException>(() => Create([item, item]));
        Assert.Throws<ArgumentNullException>(() => Create(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Hold_WhenExpirationIsNotFuture_Rejects(int minutes)
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeatHold(id, Guid.NewGuid(), Guid.NewGuid(),
            LifecycleTestData.Now.AddMinutes(minutes), [new SeatHoldItem(id, Guid.NewGuid(), 1m)], LifecycleTestData.Clock));
    }

    [Fact]
    public void HoldItem_WhenPriceIsNegativeOrIdentityIsEmpty_Rejects()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeatHoldItem(Guid.NewGuid(), Guid.NewGuid(), -0.01m));
        Assert.Throws<ArgumentException>(() => new SeatHoldItem(Guid.Empty, Guid.NewGuid(), 1m));
        Assert.Throws<ArgumentException>(() => new SeatHoldItem(Guid.NewGuid(), Guid.Empty, 1m));
    }
}
