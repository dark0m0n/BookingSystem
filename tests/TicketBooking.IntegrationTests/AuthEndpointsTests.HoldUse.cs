using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task HoldUse_EnforcesStoredExpiryWithoutWorkerCleanup(int ticksFromExpiry)
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt.AddTicks(ticksFromExpiry));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var timing = scope.ServiceProvider.GetRequiredService<BookingTimingPolicy>();

        // Do not run the expiry processor: persistence deliberately still says Active.
        using var history = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var storedHistory = (await history.Content.ReadFromJsonAsync<HoldResponse>())!;
        Assert.Equal((int)SeatHoldStatus.Active, storedHistory.Status);
        Assert.Equal(setup.Hold.ExpiresAt, storedHistory.ExpiresAt);
        foreach (var operation in new[] { "validate", "convert", "booking", "cancel" })
        {
            var hold = await db.SeatHolds.AsNoTracking().Include(value => value.Items).SingleAsync();
            Assert.Equal(SeatHoldStatus.Active, hold.Status);
            Action use = operation switch
            {
                "validate" => () => hold.EnsureCanConvert(setup.OwnerId, clock),
                "convert" => () => hold.Convert(setup.OwnerId, clock),
                "booking" => () => BookingEntity.CreateFromHold(Guid.NewGuid(), hold, setup.OwnerId, "BK-LAG",
                    clock, setup.Session.StartsAt, timing),
                _ => () => hold.Cancel(setup.OwnerId, clock)
            };
            if (ticksFromExpiry < 0)
                use();
            else
            {
                Assert.Equal(DomainError.HoldExpired, Assert.Throws<DomainRuleException>(use).Error);
                Assert.Equal(SeatHoldStatus.Active, hold.Status);
                Assert.Equal(setup.Hold.ExpiresAt, hold.ExpiresAt);
                Assert.Equal(setup.Hold.TotalAmount, hold.TotalAmount.Amount);
                Assert.Equal(setup.Hold.Currency, hold.TotalAmount.Currency);
                Assert.Equal(setup.Hold.Items.Count, hold.Items.Count);
            }
        }
        Assert.Empty(await db.Bookings.ToArrayAsync());
        await AssertOriginalHoldStillAllocated(setup.Hold);

        using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        if (ticksFromExpiry < 0)
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        else
        {
            await AssertProblem(cancelled, 409, "hold_expired");
            // Worker lag must not advertise/reallocate the still-held seats as free either.
            using var retry = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
                new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
            await AssertProblem(retry, 409, "seat_not_available");
            await AssertOriginalHoldStillAllocated(setup.Hold);
        }
    }
}
