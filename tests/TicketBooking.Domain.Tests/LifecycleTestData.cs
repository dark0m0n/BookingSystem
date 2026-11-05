using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Tests;

internal static class LifecycleTestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

    public static ManualTimeProvider Clock => new(Now);

    public static ManualTimeProvider At(DateTimeOffset instant) => new(instant);

    public static SeatHold Hold(params decimal[] prices)
    {
        var id = Guid.NewGuid();
        var snapshots = prices.Length == 0 ? new[] { 100m } : prices;
        return new SeatHold(id, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10),
            snapshots.Select(price => new SeatHoldItem(id, Guid.NewGuid(), price)), Clock);
    }

    public static BookingEntity Booking()
    {
        var hold = Hold();
        return BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", Clock);
    }
}
