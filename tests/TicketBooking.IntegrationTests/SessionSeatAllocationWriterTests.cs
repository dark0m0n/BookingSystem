using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

public class SessionSeatAllocationWriterTests
{
    [Fact]
    public async Task InvalidFencingInputs_RejectBeforeExecutingSql()
    {
        using var context = PersistenceModelTests.CreateContext();
        var writer = new SessionSeatAllocationWriter(context);

        await Assert.ThrowsAsync<ArgumentException>(() => writer.HoldAsync(Guid.Empty, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => writer.HoldAsync(Guid.NewGuid(), -1));
        await Assert.ThrowsAsync<OverflowException>(() => writer.HoldAsync(Guid.NewGuid(), long.MaxValue));
    }

    [Theory]
    [InlineData(SessionSeatStatus.Available, SessionSeatStatus.Available)]
    [InlineData(SessionSeatStatus.Unavailable, SessionSeatStatus.Available)]
    [InlineData(SessionSeatStatus.Held, SessionSeatStatus.Booked)]
    [InlineData(SessionSeatStatus.Held, SessionSeatStatus.Held)]
    public async Task Release_RejectsInvalidStatePairs(SessionSeatStatus expected, SessionSeatStatus target)
    {
        using var context = PersistenceModelTests.CreateContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new SessionSeatAllocationWriter(context)
            .ReleaseAsync(Guid.NewGuid(), 0, expected, target));
    }
}
