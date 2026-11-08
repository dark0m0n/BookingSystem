using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Tests;

public class SeatHoldCleanupReplayTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CancelReplay_AfterDeadlinePreservesHistoryButStillRequiresOwner(int ticks)
    {
        var hold = LifecycleTestData.Hold();
        hold.Cancel(hold.UserId, LifecycleTestData.Clock);
        var amount = hold.TotalAmount;
        var expires = hold.ExpiresAt;
        var clock = LifecycleTestData.At(expires.AddTicks(ticks));
        hold.Cancel(hold.UserId, clock);
        Assert.Equal(SeatHoldStatus.Cancelled, hold.Status);
        Assert.Equal(amount, hold.TotalAmount);
        Assert.Equal(expires, hold.ExpiresAt);
        Assert.Equal(DomainError.ResourceNotOwned, Assert.Throws<DomainRuleException>(() => hold.Cancel(Guid.NewGuid(), clock)).Error);
        Assert.Equal(SeatHoldStatus.Cancelled, hold.Status);
    }

    [Fact]
    public void ExpireReplay_PreservesTerminalStateAndCapturedValues()
    {
        var hold = LifecycleTestData.Hold();
        var amount = hold.TotalAmount;
        var expires = hold.ExpiresAt;
        hold.Expire(LifecycleTestData.At(expires));
        hold.Expire(LifecycleTestData.At(expires.AddMinutes(1)));
        hold.Expire(LifecycleTestData.Clock);
        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
        Assert.Equal(amount, hold.TotalAmount);
        Assert.Equal(expires, hold.ExpiresAt);
        Assert.Equal(DomainError.HoldExpired, Assert.Throws<DomainRuleException>(() => hold.Convert(hold.UserId, LifecycleTestData.Clock)).Error);
        Assert.Equal(SeatHoldStatus.Expired, hold.Status);
    }
}
