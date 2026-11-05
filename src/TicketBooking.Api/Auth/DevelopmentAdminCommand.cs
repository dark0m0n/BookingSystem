using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Auth;

public static class DevelopmentAdminCommand
{
    public const string Switch = "--initialize-admin";

    public static async Task<int> RunAsync(WebApplicationBuilder builder, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        // Check before reading credentials, registering persistence or building the host.
        if (!builder.Environment.IsDevelopment())
        {
            return Fail(error, "Admin initialization is available only in Development.");
        }

        var email = builder.Configuration["DevelopmentAdmin:Email"]?.Trim();
        var password = builder.Configuration["DevelopmentAdmin:Password"];
        var displayName = builder.Configuration["DevelopmentAdmin:DisplayName"]?.Trim() ?? "Development Admin";
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrEmpty(password) || password.Length > 128 || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200)
        {
            return Fail(error, "Set valid DevelopmentAdmin:Email and DevelopmentAdmin:Password through User Secrets or environment variables; DisplayName is optional.");
        }
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Fail(error, "Configure ConnectionStrings:DefaultConnection before initializing an admin.");
        }

        try
        {
            builder.Services.AddDbContext<TicketBookingDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddTicketBookingIdentity();
            // Building permits scoped Identity operations; this command never starts HTTP hosting.
            await using var application = builder.Build();
            await using var scope = application.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(RoleNames.Admin))
            {
                return Fail(error, "Apply the repository migrations before initializing an admin.");
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                if (!await users.IsInRoleAsync(existing, RoleNames.Admin))
                {
                    return Fail(error, "An existing non-admin account uses this email; it will not be promoted. Choose a distinct admin account.");
                }
                output.WriteLine("Development admin already exists; credentials and profile are unchanged.");
                return 0;
            }
            if (await users.FindByNameAsync(email) is not null)
            {
                return Fail(error, "An existing account uses this username; choose a distinct admin account.");
            }

            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
            if (!(await users.CreateAsync(user, password)).Succeeded)
            {
                return Fail(error, "Admin account creation failed; check Identity password rules and account configuration.");
            }
            if (!(await users.AddToRoleAsync(user, RoleNames.Admin)).Succeeded)
            {
                return Fail(error, "Admin membership could not be assigned; no account was committed.");
            }
            await transaction.CommitAsync(cancellationToken);
            output.WriteLine("Development admin initialized.");
            return 0;
        }
        catch (Exception)
        {
            // Do not print configuration, Identity descriptions or provider exception details.
            return Fail(error, "Admin initialization failed; check account configuration, Identity password rules and applied database migrations.");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }
}
