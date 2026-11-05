using Microsoft.AspNetCore.Identity;

namespace TicketBooking.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public ApplicationUser()
    {
        Id = Guid.NewGuid();
    }
}
