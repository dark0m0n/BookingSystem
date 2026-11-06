using TicketBooking.Domain.Catalogue;

namespace TicketBooking.Domain.Tests;

public class CatalogueTests
{
    [Fact]
    public void Session_UpdateSchedulePreservesIdentityAndNormalizesInstants()
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1));
        var id = session.Id;
        var eventId = Guid.NewGuid();
        var auditoriumId = Guid.NewGuid();
        var newStart = start.AddDays(1).ToOffset(TimeSpan.FromHours(2));

        session.UpdateSchedule(eventId, auditoriumId, newStart, newStart.AddHours(2));

        Assert.Equal(id, session.Id);
        Assert.Equal(eventId, session.EventId);
        Assert.Equal(auditoriumId, session.AuditoriumId);
        Assert.Equal(newStart, session.StartsAt);
        Assert.Equal(newStart.AddHours(2), session.EndsAt);
        Assert.Equal(TimeSpan.Zero, session.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, session.EndsAt.Offset);
        Assert.Equal(SessionStatus.Scheduled, session.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Session_UpdateScheduleComparesEndToStartAtTickBoundaries(long ticks)
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1));
        if (ticks > 0)
        {
            session.UpdateSchedule(session.EventId, session.AuditoriumId, start, start.AddTicks(ticks));
            Assert.Equal(start.AddTicks(ticks), session.EndsAt);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateSchedule(Guid.NewGuid(), Guid.NewGuid(), start, start.AddTicks(ticks)));
            Assert.Equal(start.AddHours(1), session.EndsAt);
            Assert.Equal(start, session.StartsAt);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Session_UpdateScheduleRejectsEmptyParentsWithoutMutation(bool emptyEvent)
    {
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1));
        var eventId = session.EventId;
        var auditoriumId = session.AuditoriumId;
        Assert.Throws<ArgumentException>(() => session.UpdateSchedule(emptyEvent ? Guid.Empty : Guid.NewGuid(),
            emptyEvent ? Guid.NewGuid() : Guid.Empty, start.AddDays(1), start.AddDays(1).AddHours(2)));
        Assert.Equal(eventId, session.EventId);
        Assert.Equal(auditoriumId, session.AuditoriumId);
        Assert.Equal(start, session.StartsAt);
        Assert.Equal(start.AddHours(1), session.EndsAt);
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Archived)]
    public void Event_UpdateDetailsPreservesIdentityAndAcceptsDefinedStatuses(EventStatus status)
    {
        var offering = new Event(Guid.NewGuid(), "Film", "Description", TimeSpan.FromMinutes(90), 12);
        var id = offering.Id;

        offering.UpdateDetails("Updated", "", TimeSpan.FromMinutes(120), 0, status);

        Assert.Equal(id, offering.Id);
        Assert.Equal("Updated", offering.Title);
        Assert.Equal("", offering.Description);
        Assert.Equal(TimeSpan.FromMinutes(120), offering.Duration);
        Assert.Equal(0, offering.AgeRating);
        Assert.Equal(status, offering.Status);
    }

    [Theory]
    [InlineData(null, "Changed", 120, 0, EventStatus.Published)]
    [InlineData(" ", "Changed", 120, 0, EventStatus.Published)]
    [InlineData("Changed", null, 120, 0, EventStatus.Published)]
    [InlineData("Changed", "Changed", 0, 0, EventStatus.Published)]
    [InlineData("Changed", "Changed", -1, 0, EventStatus.Published)]
    [InlineData("Changed", "Changed", 120, -1, EventStatus.Published)]
    [InlineData("Changed", "Changed", 120, 0, (EventStatus)999)]
    public void Event_InvalidUpdateLeavesAllFieldsUnchanged(string? title, string? description, int minutes, int age, EventStatus status)
    {
        var offering = new Event(Guid.NewGuid(), "Film", "Description", TimeSpan.FromMinutes(90), 12);

        Assert.ThrowsAny<ArgumentException>(() => offering.UpdateDetails(title!, description!, TimeSpan.FromMinutes(minutes), age, status));

        Assert.Equal("Film", offering.Title);
        Assert.Equal("Description", offering.Description);
        Assert.Equal(TimeSpan.FromMinutes(90), offering.Duration);
        Assert.Equal(12, offering.AgeRating);
        Assert.Equal(EventStatus.Draft, offering.Status);
    }

    [Fact]
    public void Catalogue_RepresentsAnEventSessionAndPricedSeat()
    {
        var offering = new Event(Guid.NewGuid(), "Film", "Description", TimeSpan.FromMinutes(90), 12);
        var start = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.FromHours(2));
        var session = new Session(Guid.NewGuid(), offering.Id, Guid.NewGuid(), start, start.Add(offering.Duration));
        var seatId = Guid.NewGuid();
        var seat = new SessionSeat(Guid.NewGuid(), session.Id, seatId, LifecycleTestData.Price(125.50m));

        Assert.Equal(EventStatus.Draft, offering.Status);
        Assert.Equal(offering.Id, session.EventId);
        Assert.Equal(TimeSpan.FromMinutes(90), session.EndsAt - session.StartsAt);
        Assert.Equal(SessionStatus.Scheduled, session.Status);
        Assert.Equal(session.Id, seat.SessionId);
        Assert.Equal(seatId, seat.SeatId);
        Assert.Equal(LifecycleTestData.Price(125.50m), seat.Price);
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
        var seat = new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LifecycleTestData.Price(0m), status, 7);

        Assert.Equal(status, seat.Status);
        Assert.Equal(LifecycleTestData.Price(0m), seat.Price);
        Assert.Equal(7L, seat.Version);
    }

    [Fact]
    public void SessionSeat_WhenPriceIsMissingOrVersionIsNegative_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LifecycleTestData.Price(1m), version: -1));
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
            new SessionSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LifecycleTestData.Price(1m), (SessionSeatStatus)99));
    }
}
