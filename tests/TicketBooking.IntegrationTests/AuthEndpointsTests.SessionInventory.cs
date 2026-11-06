using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Persistence;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData("UAH", "0")]
    [InlineData("UAH", "0.01")]
    [InlineData("UAH", "9999999999999999.99")]
    [InlineData("EUR", "0")]
    [InlineData("EUR", "0.01")]
    [InlineData("EUR", "9999999999999999.99")]
    [InlineData("USD", "0")]
    [InlineData("USD", "0.01")]
    [InlineData("USD", "9999999999999999.99")]
    public async Task SessionCreation_GeneratesIndependentInventoryWithExactPersistedSeatPrices(string currency, string amount)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var price = decimal.Parse(amount, CultureInfo.InvariantCulture);
        var first = await CreateAdmin<SessionResponse>("/api/admin/sessions", token,
            SessionRequest(setup) with { Price = price, Currency = $" {currency.ToLowerInvariant()} " });
        var second = await CreateAdmin<SessionResponse>("/api/admin/sessions", token,
            SessionRequest(setup) with { Price = 12.34m, Currency = currency });

        Assert.Equal(setup.Seats.Select(s => s.Id).Order(), first.Seats.Select(s => s.SeatId).Order());
        Assert.Equal(first.Seats.Select(s => s.SeatId).Order(), second.Seats.Select(s => s.SeatId).Order());
        Assert.Equal(setup.Seats.Length * 2, first.Seats.Concat(second.Seats).Select(s => s.Id).Distinct().Count());
        Assert.All(first.Seats, s =>
        {
            Assert.Equal(price, s.Price);
            Assert.Equal(currency, s.Currency);
            Assert.Equal((int)SessionSeatStatus.Available, s.Status);
            Assert.Equal(0, s.Version);
        });

        // Reload through a separate scope to verify database precision and independent Money captures.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.SessionSeats.OrderBy(s => s.Id).ToArrayAsync();
        Assert.Equal(setup.Seats.Length * 2, stored.Length);
        var firstRows = stored.Where(s => s.SessionId == first.Id).ToArray();
        Assert.Equal(setup.Seats.Length, firstRows.Length);
        Assert.All(firstRows, s => Assert.Equal(new Money(price, currency), s.Price));
        Assert.All(stored.Where(s => s.SessionId == second.Id), s => Assert.Equal(new Money(12.34m, currency), s.Price));
        Assert.NotSame(firstRows[0].Price, firstRows[1].Price);

        // Changing one tracked complex price must affect neither sibling seats nor another session.
        db.Entry(firstRows[0]).ComplexProperty(s => s.Price).Property(p => p.Amount).CurrentValue = 5m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.SessionSeats.AsNoTracking().ToArrayAsync();
        Assert.Equal(5m, reloaded.Single(s => s.Id == firstRows[0].Id).Price.Amount);
        Assert.All(reloaded.Where(s => s.SessionId == first.Id && s.Id != firstRows[0].Id), s => Assert.Equal(price, s.Price.Amount));
        Assert.All(reloaded.Where(s => s.SessionId == second.Id), s => Assert.Equal(12.34m, s.Price.Amount));
    }
}
