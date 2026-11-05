using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IdentityBuilder AddTicketBookingIdentity(this IServiceCollection services) =>
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TicketBookingDbContext>();
}
