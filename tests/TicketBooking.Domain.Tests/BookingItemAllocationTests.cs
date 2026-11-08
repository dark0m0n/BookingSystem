using TicketBooking.Domain.Booking;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Tests;

public class BookingItemAllocationTests
{
    [Fact]
    public void BookingCapture_RetainsNewTokenIndependentlyOfSourceHold()
    {
        var hold = LifecycleTestData.Hold();
        Assert.Single(hold.Items).CaptureAllocationVersion(1);
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-CAPTURE",
            LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
        var item = Assert.Single(booking.Items);
        Assert.Null(item.AllocationVersion);
        item.CaptureAllocationVersion(2);
        Assert.Equal(2, item.AllocationVersion);
        Assert.Equal(1, Assert.Single(hold.Items).AllocationVersion);
        Assert.Throws<InvalidOperationException>(() => item.CaptureAllocationVersion(3));
        Assert.Equal(2, item.AllocationVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capture_RejectsInvalidTokenWithoutChangingItem(long version)
    {
        var item = new BookingItem(Guid.NewGuid(), Guid.NewGuid(), new Money(10m, "UAH"));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.CaptureAllocationVersion(version));
        Assert.Null(item.AllocationVersion);
        Assert.Equal(new Money(10m, "UAH"), item.PriceSnapshot);
    }
}
