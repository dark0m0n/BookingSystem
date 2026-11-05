using Microsoft.EntityFrameworkCore;

namespace TicketBooking.Infrastructure.Persistence;

public sealed class SeatAllocationConflictException() : DbUpdateConcurrencyException(
    "The session seat no longer has the expected allocation version and state.")
{
}
