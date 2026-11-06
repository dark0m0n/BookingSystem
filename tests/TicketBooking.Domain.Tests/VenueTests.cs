using TicketBooking.Domain.Venue;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.Domain.Tests;

public class VenueTests
{
    [Fact]
    public void Seat_UpdateLayoutPreservesIdentityAndAuditorium()
    {
        var seat = new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 1);
        var id = seat.Id;
        var auditoriumId = seat.AuditoriumId;

        seat.UpdateLayout("B", 2, SeatType.Accessible);

        Assert.Equal(id, seat.Id);
        Assert.Equal(auditoriumId, seat.AuditoriumId);
        Assert.Equal("B", seat.Row);
        Assert.Equal(2, seat.Number);
        Assert.Equal(SeatType.Accessible, seat.Type);
    }

    [Theory]
    [InlineData(null, 2, SeatType.Premium)]
    [InlineData(" ", 2, SeatType.Premium)]
    [InlineData("B", 0, SeatType.Premium)]
    [InlineData("B", -1, SeatType.Premium)]
    [InlineData("B", 2, (SeatType)999)]
    public void Seat_UpdateLayoutRejectsInvalidValuesWithoutChangingState(string? row, int number, SeatType type)
    {
        var seat = new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 1);

        Assert.ThrowsAny<ArgumentException>(() => seat.UpdateLayout(row!, number, type));

        Assert.Equal("A", seat.Row);
        Assert.Equal(1, seat.Number);
        Assert.Equal(SeatType.Standard, seat.Type);
    }

    [Fact]
    public void Auditorium_RenamePreservesItsIdentityAndVenue()
    {
        var auditorium = new Auditorium(Guid.NewGuid(), Guid.NewGuid(), "Hall");
        var originalId = auditorium.Id;
        var venueId = auditorium.VenueId;

        auditorium.Rename("Renamed");

        Assert.Equal("Renamed", auditorium.Name);
        Assert.Equal(originalId, auditorium.Id);
        Assert.Equal(venueId, auditorium.VenueId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Auditorium_RenameRejectsBlankNameWithoutChangingState(string? name)
    {
        var auditorium = new Auditorium(Guid.NewGuid(), Guid.NewGuid(), "Hall");

        Assert.ThrowsAny<ArgumentException>(() => auditorium.Rename(name!));

        Assert.Equal("Hall", auditorium.Name);
    }

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
