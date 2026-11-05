using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly CustomerRoleWriteFailure customerRoleWriteFailure = new();

    [PostgresFact]
    public async Task RoleMigration_CreatesOnlyDefinitionsAndIsIdempotent()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var customer = (await roles.FindByNameAsync(RoleNames.Customer))!;
        var admin = (await roles.FindByNameAsync(RoleNames.Admin))!;
        Assert.NotNull(customer);
        Assert.NotNull(admin);
        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.NotEqual(Guid.Empty, admin.Id);

        await database.Database.MigrateAsync();
        database.ChangeTracker.Clear();

        Assert.Equal(2, await database.Roles.CountAsync());
        Assert.Equal(customer.Id, (await roles.FindByNameAsync(RoleNames.Customer))!.Id);
        Assert.Equal(admin.Id, (await roles.FindByNameAsync(RoleNames.Admin))!.Id);
        Assert.Empty(await database.Users.ToArrayAsync());
        Assert.Empty(await database.UserRoles.ToArrayAsync());
    }

    [PostgresFact]
    public async Task RoleMigration_PreservesExistingIdsAndMembershipsWhileCanonicalizingNames()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261105165909_AddRefreshTokenRotation");
        foreach (var role in await roles.Roles.ToArrayAsync())
        {
            Assert.True((await roles.DeleteAsync(role)).Succeeded);
        }
        var customer = new IdentityRole<Guid>("customer") { Id = Guid.NewGuid() };
        var admin = new IdentityRole<Guid>("ADMIN") { Id = Guid.NewGuid() };
        Assert.True((await roles.CreateAsync(customer)).Succeeded);
        Assert.True((await roles.CreateAsync(admin)).Succeeded);
        var existing = new ApplicationUser { UserName = "legacy", Email = "legacy@example.test" };
        Assert.True((await users.CreateAsync(existing, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(existing, RoleNames.Admin)).Succeeded);

        await migrator.MigrateAsync();
        database.ChangeTracker.Clear();

        Assert.Equal(2, await database.Roles.CountAsync());
        var currentCustomer = (await roles.FindByNameAsync(RoleNames.Customer))!;
        var currentAdmin = (await roles.FindByNameAsync(RoleNames.Admin))!;
        Assert.Equal(customer.Id, currentCustomer.Id);
        Assert.Equal(admin.Id, currentAdmin.Id);
        Assert.Equal(RoleNames.Customer, currentCustomer.Name);
        Assert.Equal(RoleNames.Admin, currentAdmin.Name);
        Assert.Equal(admin.Id, (await database.UserRoles.SingleAsync()).RoleId);
        Assert.Equal(new[] { RoleNames.Admin }, await users.GetRolesAsync(existing));
    }

    [PostgresTheory]
    [InlineData("role")]
    [InlineData("roles")]
    [InlineData("isAdmin")]
    [InlineData("permissions")]
    public async Task PublicRegistration_AssignsOnlyCustomerDespiteRequestedAdminFields(string field)
    {
        var payload = new Dictionary<string, object>
        {
            ["email"] = "alice@example.test",
            ["password"] = Password,
            ["displayName"] = "Alice",
            [field] = field switch { "isAdmin" => true, "role" => "Admin", _ => new[] { "Admin" } }
        };

        using var registered = await client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("alice@example.test"))!;
            Assert.Equal(new[] { RoleNames.Customer }, await users.GetRolesAsync(user));
            Assert.False(await users.IsInRoleAsync(user, RoleNames.Admin));
        }
        var login = await LoginToken();
        Assert.Equal(new[] { RoleNames.Customer }, new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken)
            .Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value));
        using var customer = await AuthorizedRequest("/test/customer", login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, customer.StatusCode);
        using var admin = await Admin(login.AccessToken);
        await AssertProblem(admin, 403, "forbidden");
        var refreshed = await RotateToken(login.RefreshToken);
        using var refreshedAdmin = await Admin(refreshed.AccessToken);
        await AssertProblem(refreshedAdmin, 403, "forbidden");
    }

    [PostgresFact]
    public async Task CustomerRoleAssignmentFailure_RollsBackTheAccountAndAllowsRegistrationRetry()
    {
        customerRoleWriteFailure.FailNext();

        using var failed = await Register();

        await AssertProblem(failed, 500, "internal_error");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Empty(await database.Users.ToArrayAsync());
            Assert.Empty(await database.UserRoles.ToArrayAsync());
            Assert.Equal(2, await database.Roles.CountAsync());
        }
        using var retry = await Register();
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var login = await LoginToken();
        using var customer = await AuthorizedRequest("/test/customer", login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, customer.StatusCode);
        using var admin = await Admin(login.AccessToken);
        await AssertProblem(admin, 403, "forbidden");
    }

    [PostgresFact]
    public async Task TrustedAdminAssignment_GrantsBothPoliciesAndRefreshReflectsRemoval()
    {
        using var registered = await Register();
        var login = await LoginToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("alice@example.test"))!;
            Assert.True((await users.AddToRoleAsync(user, RoleNames.Admin)).Succeeded);
            Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.Customer)).Succeeded);
        }
        var adminOnly = await RotateToken(login.RefreshToken);
        using var customer = await AuthorizedRequest("/test/customer", adminOnly.AccessToken);
        using var admin = await Admin(adminOnly.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, customer.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, admin.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByEmailAsync("alice@example.test"))!, RoleNames.Admin)).Succeeded);
        }
        var removed = await RotateToken(adminOnly.RefreshToken);
        using var deniedCustomer = await AuthorizedRequest("/test/customer", removed.AccessToken);
        using var deniedAdmin = await Admin(removed.AccessToken);
        await AssertProblem(deniedCustomer, 403, "forbidden");
        await AssertProblem(deniedAdmin, 403, "forbidden");

        // Issued access claims remain a snapshot; role removal takes effect on new tokens.
        using var priorAdmin = await Admin(adminOnly.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, priorAdmin.StatusCode);
        clock.Set(adminOnly.ExpiresAt);
        using var expiredAdmin = await Admin(adminOnly.AccessToken);
        await AssertProblem(expiredAdmin, 401, "unauthorized");
    }

    [PostgresFact]
    public async Task AuthenticatedRolelessUser_CannotUseEitherPolicy()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser
            {
                UserName = "alice@example.test",
                Email = "alice@example.test",
                DisplayName = "Legacy"
            }, Password)).Succeeded);
        }
        var login = await LoginToken();
        using var customer = await AuthorizedRequest("/test/customer", login.AccessToken);
        using var admin = await Admin(login.AccessToken);
        await AssertProblem(customer, 403, "forbidden");
        await AssertProblem(admin, 403, "forbidden");
    }

    [PostgresTheory]
    [InlineData("/test/customer")]
    [InlineData("/test/admin")]
    public async Task NamedPolicies_ChallengeAnonymousRequests(string route)
    {
        using var response = await client.GetAsync(route);

        await AssertProblem(response, 401, "unauthorized");
    }

    private sealed class CustomerRoleWriteFailure : SaveChangesInterceptor
    {
        private int failNext;
        public void FailNext() => Interlocked.Exchange(ref failNext, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<Guid>>().Any(entry => entry.State == EntityState.Added) &&
                Interlocked.Exchange(ref failNext, 0) == 1)
            {
                throw new InvalidOperationException("Test-only Customer membership failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
