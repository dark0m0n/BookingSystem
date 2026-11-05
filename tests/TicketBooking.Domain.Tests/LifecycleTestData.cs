using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Domain.Tests;

internal static class LifecycleTestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset SessionStartsAt = Now.AddHours(2);
    public static BookingTimingPolicy TimingPolicy => new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));

    public static ManualTimeProvider Clock => new(Now);

    public static ManualTimeProvider At(DateTimeOffset instant) => new(instant);

    public static Money Price(decimal amount, string currency = "UAH") => new(amount, currency);

    public static SessionSeat Seat(Guid sessionId, decimal amount, string currency = "UAH",
        SessionSeatStatus status = SessionSeatStatus.Available) =>
        new(Guid.NewGuid(), sessionId, Guid.NewGuid(), Price(amount, currency), status);

    public static SeatHold Hold(params decimal[] prices)
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var snapshots = prices.Length == 0 ? new[] { 100m } : prices;
        return new SeatHold(id, sessionId, Guid.NewGuid(), SessionStartsAt, snapshots.Select(price => Seat(sessionId, price)), Clock, LifecycleTestData.TimingPolicy);
    }

    public static BookingEntity Booking(params decimal[] prices)
    {
        var hold = Hold(prices);
        return BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-001", Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
    }
}
