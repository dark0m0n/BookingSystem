namespace TicketBooking.Application.Identity;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
