using TicketBooking.Domain.Venue;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.Domain.Tests;

public class VenueTests
{
    [Fact]
    public void Layout_ReferencesItsOwningVenueAndAuditoriumById()
    {
        var venue = new VenueEntity(Guid.NewGuid(), "Cinema", "Main Street 1");
        var auditorium = new Auditorium(Guid.NewGuid(), venue.Id, "Hall 1");
        var seat = new Seat(Guid.NewGuid(), auditorium.Id, "A", 1, SeatType.Accessible);

        Assert.Equal(venue.Id, auditorium.VenueId);
        Assert.Equal(auditorium.Id, seat.AuditoriumId);
        Assert.Equal("A", seat.Row);
        Assert.Equal(1, seat.Number);
        Assert.Equal(SeatType.Accessible, seat.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Venue_WhenNameIsMissing_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new VenueEntity(Guid.NewGuid(), name!, "Address"));
    }

    [Fact]
    public void Layout_WhenAnIdentifierIsEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new VenueEntity(Guid.Empty, "Cinema", "Address"));
        Assert.Throws<ArgumentException>(() => new Auditorium(Guid.NewGuid(), Guid.Empty, "Hall"));
        Assert.Throws<ArgumentException>(() => new Seat(Guid.NewGuid(), Guid.Empty, "A", 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Seat_WhenNumberIsNotPositive_Throws(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", number));
    }

    [Fact]
    public void Seat_WhenTypeIsUndefined_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 1, (SeatType)99));
    }
}
