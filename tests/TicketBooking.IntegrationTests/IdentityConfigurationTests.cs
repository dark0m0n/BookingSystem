using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

public class IdentityConfigurationTests
{
    [Fact]
    public void Registration_ResolvesGuidUserAndRoleManagersWithEfStores()
    {
        using var services = CreateServices("Host=localhost;Database=metadata_only;Username=test");
        using var scope = services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
        var store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();
        Assert.IsAssignableFrom<IUserPasswordStore<ApplicationUser>>(store);
        Assert.IsAssignableFrom<IUserRoleStore<ApplicationUser>>(store);
        Assert.True(scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.User.RequireUniqueEmail);
        Assert.NotEqual(Guid.Empty, new ApplicationUser().Id);
    }

    [Fact]
    public void Model_UsesIdentityTablesGuidKeysAndNormalizedNameUniqueness()
    {
        using var context = PersistenceModelTests.CreateContext();
        var user = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var role = context.Model.FindEntityType(typeof(IdentityRole<Guid>))!;

        Assert.Equal("AspNetUsers", user.GetTableName());
        Assert.Equal("AspNetRoles", role.GetTableName());
        Assert.Equal(typeof(Guid), Assert.Single(user.FindPrimaryKey()!.Properties).ClrType);
        Assert.True(user.FindProperty(nameof(ApplicationUser.ConcurrencyStamp))!.IsConcurrencyToken);
        Assert.Contains(user.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(ApplicationUser.NormalizedUserName)]));
        Assert.Contains(role.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(IdentityRole<Guid>.NormalizedName)]));
        Assert.Null(context.Model.FindEntityType(typeof(TicketBooking.Domain.Booking.Booking))!
            .GetForeignKeys().SingleOrDefault(key => key.Properties.Any(property => property.Name == "UserId")));
    }

    internal static ServiceProvider CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<TicketBookingDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddTicketBookingIdentity();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
