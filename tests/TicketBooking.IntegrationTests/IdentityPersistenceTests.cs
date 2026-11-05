using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public class IdentityPersistenceTests : IAsyncLifetime
{
    private const string Password = "Identity-test-Password1!";
    private readonly string schema = "identity_tests_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_CONNECTION_STRING")!;
    private ServiceProvider services = null!;

    public async Task InitializeAsync()
    {
        await ExecuteSchemaCommand($"CREATE SCHEMA \"{schema}\"");
        var testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema }.ConnectionString;
        services = IdentityConfigurationTests.CreateServices(testConnection);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }
        await ExecuteSchemaCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
    }

    [PostgresFact]
    public async Task CreateUser_PersistsNormalizedIdentityAndPasswordHashAcrossScopes()
    {
        Guid id;
        await using (var scope = services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = NewUser("Alice", "Alice@example.test");
            AssertSucceeded(await manager.CreateAsync(user, Password));
            id = user.Id;
        }

        await using var reload = services.CreateAsyncScope();
        var reloadedManager = reload.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = Assert.IsType<ApplicationUser>(await reloadedManager.FindByEmailAsync("alice@EXAMPLE.test"));
        Assert.Equal(id, stored.Id);
        Assert.Equal("ALICE", stored.NormalizedUserName);
        Assert.Equal("ALICE@EXAMPLE.TEST", stored.NormalizedEmail);
        Assert.NotNull(stored.PasswordHash);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(await reloadedManager.CheckPasswordAsync(stored, Password));
        Assert.False(await reloadedManager.CheckPasswordAsync(stored, "incorrect-password"));
        Assert.Equal(id, (await reloadedManager.FindByIdAsync(id.ToString()))!.Id);
    }

    [PostgresTheory]
    [InlineData("ALICE", "other@example.test", "DuplicateUserName")]
    [InlineData("OtherUser", "ALICE@EXAMPLE.TEST", "DuplicateEmail")]
    public async Task CreateUser_RejectsNormalizedDuplicates(string name, string email, string expectedError)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        AssertSucceeded(await manager.CreateAsync(NewUser("Alice", "alice@example.test"), Password));

        var result = await manager.CreateAsync(NewUser(name, email), Password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == expectedError);
        Assert.Equal(1, await manager.Users.CountAsync());
    }

    [PostgresFact]
    public async Task Database_RejectsDuplicateNormalizedEmailsWithDifferentUserNames()
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        AssertSucceeded(await users.CreateAsync(NewUser("Alice", "alice@example.test"), Password));
        var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        context.Users.Add(new ApplicationUser
        {
            UserName = "OtherUser",
            NormalizedUserName = "OTHERUSER",
            Email = "ALICE@EXAMPLE.TEST",
            NormalizedEmail = "ALICE@EXAMPLE.TEST"
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("EmailIndex", postgres.ConstraintName);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [PostgresFact]
    public async Task CreateUser_RejectsWeakPasswordsWithoutPersistingTheUser()
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await manager.CreateAsync(NewUser("Alice", "alice@example.test"), "weak");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code.StartsWith("Password", StringComparison.Ordinal));
        Assert.Empty(await manager.Users.ToArrayAsync());
    }

    [PostgresFact]
    public async Task RolesAndClaims_PersistAndReloadThroughIdentityManagers()
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True(await roles.RoleExistsAsync("Customer"));
            var user = NewUser("Alice", "alice@example.test");
            AssertSucceeded(await users.CreateAsync(user, Password));
            AssertSucceeded(await users.AddToRoleAsync(user, "Customer"));
            AssertSucceeded(await users.AddClaimAsync(user, new Claim("test-permission", "test-value")));
        }

        await using var reload = services.CreateAsyncScope();
        var manager = reload.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = (await manager.FindByNameAsync("alice"))!;
        Assert.True(await manager.IsInRoleAsync(stored, "customer"));
        Assert.Contains(await manager.GetClaimsAsync(stored), claim => claim.Type == "test-permission" && claim.Value == "test-value");
    }

    [PostgresFact]
    public async Task ConcurrentUserUpdates_ReturnIdentityConcurrencyFailure()
    {
        await using var firstScope = services.CreateAsyncScope();
        var firstManager = firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var firstUser = NewUser("Alice", "alice@example.test");
        AssertSucceeded(await firstManager.CreateAsync(firstUser, Password));
        await using var staleScope = services.CreateAsyncScope();
        var staleManager = staleScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staleUser = (await staleManager.FindByIdAsync(firstUser.Id.ToString()))!;
        firstUser.PhoneNumber = "111111";
        AssertSucceeded(await firstManager.UpdateAsync(firstUser));
        staleUser.PhoneNumber = "222222";

        var result = await staleManager.UpdateAsync(staleUser);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ConcurrencyFailure");
        await using var reload = services.CreateAsyncScope();
        Assert.Equal("111111", (await reload.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(firstUser.Id.ToString()))!.PhoneNumber);
    }

    private static ApplicationUser NewUser(string name, string email) => new() { UserName = name, Email = email };

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Code)));

    private async Task ExecuteSchemaCommand(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
