using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketBooking.Api.Auth;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public class DevelopmentAdminPersistenceTests : IAsyncLifetime
{
    private const string Email = "development-admin@example.test";
    private const string Password = "Test-only-Admin-Password1!";
    private readonly string schema = "admin_init_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_CONNECTION_STRING")!;
    private string testConnection = null!;

    public async Task InitializeAsync()
    {
        await SchemaCommand($"CREATE SCHEMA \"{schema}\"");
        // A one-shot command owns no long-lived host; keep its test connection pool disabled.
        testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema, Pooling = false }.ConnectionString;
        await using var context = PersistenceModelTests.CreateContext(testConnection);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => SchemaCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");

    [PostgresFact]
    public async Task CommandLine_DevelopmentCreatesAdminAndExitsWithoutStartingHttp()
    {
        var result = await DevelopmentAdminCommandTests.RunCommandLine("Development", testConnection);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Development admin initialized", result.Output);
        Assert.Empty(result.Error);
        Assert.DoesNotContain(Email, result.Output);
        Assert.DoesNotContain(Password, result.Output);
        await using var services = IdentityConfigurationTests.CreateServices(testConnection);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(Email))!;
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(new[] { RoleNames.Admin }, await users.GetRolesAsync(user));
    }

    [PostgresFact]
    public async Task Initialize_CreatesHashedAdminOnlyWithoutJwtConfigurationOrHttpHosting()
    {
        var builder = DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(builder, output, error);

        Assert.Equal(0, result);
        Assert.Contains("Development admin initialized", output.ToString());
        Assert.Empty(error.ToString());
        Assert.DoesNotContain(Email, output.ToString());
        Assert.DoesNotContain(Password, output.ToString());
        await using var services = IdentityConfigurationTests.CreateServices(testConnection);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(Email))!;
        Assert.NotNull(user);
        Assert.Equal("Development Admin", user.DisplayName);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(new[] { RoleNames.Admin }, await users.GetRolesAsync(user));
        Assert.Equal(1, await users.Users.CountAsync());
    }

    [PostgresFact]
    public async Task RepeatedInitialization_PreservesExistingAdminPasswordIdAndProfile()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await DevelopmentAdminCommand.RunAsync(
            DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection), output, error));
        await using var context = PersistenceModelTests.CreateContext(testConnection);
        var original = await context.Users.AsNoTracking().SingleAsync();
        var builder = DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection,
            " DEVELOPMENT-ADMIN@EXAMPLE.TEST ", "Different-ignored-Password2!");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DevelopmentAdmin:DisplayName"] = "Replacement" });

        var result = await DevelopmentAdminCommand.RunAsync(builder, output, error);

        Assert.Equal(0, result);
        Assert.Contains("already exists", output.ToString());
        var reloaded = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, reloaded.Id);
        Assert.Equal(original.PasswordHash, reloaded.PasswordHash);
        Assert.Equal(original.DisplayName, reloaded.DisplayName);
        Assert.Equal(original.SecurityStamp, reloaded.SecurityStamp);
    }

    [PostgresFact]
    public async Task ExistingCustomer_IsNotPromotedOrGivenTheConfiguredPassword()
    {
        await using var services = IdentityConfigurationTests.CreateServices(testConnection);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customer = new ApplicationUser { UserName = Email, Email = Email, DisplayName = "Customer" };
        Assert.True((await users.CreateAsync(customer, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(customer, RoleNames.Customer)).Succeeded);
        var originalHash = customer.PasswordHash;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(DevelopmentAdminCommandTests.CreateBuilder(
            "Development", testConnection, Email, "Different-ignored-Password2!"), output, error);

        Assert.Equal(1, result);
        Assert.Contains("will not be promoted", error.ToString());
        Assert.DoesNotContain(Email, error.ToString());
        scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().ChangeTracker.Clear();
        var stored = (await users.FindByEmailAsync(Email))!;
        Assert.Equal(originalHash, stored.PasswordHash);
        Assert.Equal(new[] { RoleNames.Customer }, await users.GetRolesAsync(stored));
        Assert.Equal(1, await users.Users.CountAsync());
    }

    [PostgresFact]
    public async Task ExistingUsernameWithAnotherEmail_IsNotReassignedOrPromoted()
    {
        await using var services = IdentityConfigurationTests.CreateServices(testConnection);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = new ApplicationUser { UserName = Email, Email = "other@example.test" };
        Assert.True((await users.CreateAsync(existing, Password)).Succeeded);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(
            DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection), output, error);

        Assert.Equal(1, result);
        Assert.Contains("username", error.ToString());
        Assert.Empty(await users.GetRolesAsync(existing));
        Assert.Equal(1, await users.Users.CountAsync());
    }

    [PostgresFact]
    public async Task WeakPassword_IsRejectedWithoutPersistingAUserOrEchoingIt()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(DevelopmentAdminCommandTests.CreateBuilder(
            "Development", testConnection, Email, "weak"), output, error);

        Assert.Equal(1, result);
        Assert.DoesNotContain("weak", error.ToString());
        Assert.DoesNotContain(Email, error.ToString());
        await using var context = PersistenceModelTests.CreateContext(testConnection);
        Assert.Empty(await context.Users.ToArrayAsync());
        Assert.Empty(await context.UserRoles.ToArrayAsync());
    }

    [PostgresFact]
    public async Task FailedRoleAssignment_RollsBackTheUserAndReportsOnlyASafeError()
    {
        var builder = DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection);
        builder.Services.AddDbContext<TicketBookingDbContext>(options => options.AddInterceptors(new AdminRoleWriteFailure()));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(builder, output, error);

        Assert.Equal(1, result);
        Assert.DoesNotContain(Email, error.ToString());
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain("private-test-diagnostic", error.ToString());
        await using var context = PersistenceModelTests.CreateContext(testConnection);
        Assert.Empty(await context.Users.ToArrayAsync());
        Assert.Empty(await context.UserRoles.ToArrayAsync());
    }

    [PostgresFact]
    public async Task MissingRoleData_DoesNotCreateFallbackRolesOrAnAccount()
    {
        await using (var services = IdentityConfigurationTests.CreateServices(testConnection))
        await using (var scope = services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.DeleteAsync((await roles.FindByNameAsync(RoleNames.Admin))!)).Succeeded);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(
            DevelopmentAdminCommandTests.CreateBuilder("Development", testConnection), output, error);

        Assert.Equal(1, result);
        Assert.Contains("migrations", error.ToString());
        await using var context = PersistenceModelTests.CreateContext(testConnection);
        Assert.Empty(await context.Users.ToArrayAsync());
        Assert.Equal(1, await context.Roles.CountAsync());
    }

    private async Task SchemaCommand(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class AdminRoleWriteFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<Guid>>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("private-test-diagnostic");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
