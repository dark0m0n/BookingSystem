using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Tests;

public class SeatHoldItemAllocationTests
{
    [Fact]
    public void CaptureAllocationVersion_RetainsReturnedTokenAndRejectsReplacement()
    {
        var item = new SeatHoldItem(Guid.NewGuid(), Guid.NewGuid(), new Money(12m, "UAH"));
        Assert.Null(item.AllocationVersion);

        item.CaptureAllocationVersion(7);

        Assert.Equal(7, item.AllocationVersion);
        Assert.Throws<InvalidOperationException>(() => item.CaptureAllocationVersion(8));
        Assert.Equal(7, item.AllocationVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CaptureAllocationVersion_RejectsInvalidTokenWithoutMutation(long version)
    {
        var item = new SeatHoldItem(Guid.NewGuid(), Guid.NewGuid(), new Money(12m, "UAH"));

        Assert.Throws<ArgumentOutOfRangeException>(() => item.CaptureAllocationVersion(version));

        Assert.Null(item.AllocationVersion);
    }
}
