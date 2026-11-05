using TicketBooking.Domain.Catalogue;

namespace TicketBooking.Domain.Tests;

public class CatalogueTests
{
    [Fact]
    public void Catalogue_RepresentsAnEventSessionAndPricedSeat()
    {
        var offering = new Event(Guid.NewGuid(), "Film", "Description", TimeSpan.FromMinutes(90), 12);
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.FromHours(2));
        var session = new Session(Guid.NewGuid(), offering.Id, Guid.NewGuid(), start, start.Add(offering.Duration));
        var seatId = Guid.NewGuid();
        var seat = new SessionSeat(Guid.NewGuid(), session.Id, seatId, 125.50m);

        Assert.Equal(EventStatus.Draft, offering.Status);
        Assert.Equal(offering.Id, session.EventId);
        Assert.Equal(TimeSpan.FromMinutes(90), session.EndsAt - session.StartsAt);
        Assert.Equal(SessionStatus.Scheduled, session.Status);
        Assert.Equal(session.Id, seat.SessionId);
        Assert.Equal(seatId, seat.SeatId);
        Assert.Equal(125.50m, seat.Price);
        Assert.Equal(SessionSeatStatus.Available, seat.Status);
        Assert.Equal(0L, seat.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Event_WhenDurationIsNotPositive_Throws(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(minutes), 0));
    }

    [Fact]
    public void Event_WhenAgeRatingIsNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(90), -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Session_WhenEndIsNotAfterStart_Throws(int minutes)
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(minutes)));
    }

    [Fact]
    public void Session_WhenEndUsesADifferentOffset_ComparesInstants()
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.FromHours(2));
        var end = new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.Zero);

        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);

        Assert.Equal(TimeSpan.FromHours(1), session.EndsAt - session.StartsAt);
    }

    [Theory]
    [InlineData(SessionSeatStatus.Available)]
    [InlineData(SessionSeatStatus.Held)]
    [InlineData(SessionSeatStatus.Booked)]
    [InlineData(SessionSeatStatus.Unavailable)]
    public void SessionSeat_AcceptsEachSpecifiedStatus(SessionSeatStatus status)
    {
        var seat = new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, status, 7);

        Assert.Equal(status, seat.Status);
        Assert.Equal(0m, seat.Price);
        Assert.Equal(7L, seat.Version);
    }

    [Fact]
    public void SessionSeat_WhenPriceOrVersionIsNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, version: -1));
    }

    [Fact]
    public void Catalogue_WhenStatusIsUndefined_Throws()
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(90), 0, (EventStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1), (SessionStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, (SessionSeatStatus)99));
    }
}
