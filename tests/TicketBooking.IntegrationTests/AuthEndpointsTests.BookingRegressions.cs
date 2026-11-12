using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Api.Configuration;
using TicketBooking.Application.Identity;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Booking;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task BookingRegression_ExpiredHoldCannotConvertOrDisturbReacquiredSeats()
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt);
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        using var acquired = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, acquired.StatusCode);
        var successor = (await acquired.Content.ReadFromJsonAsync<HoldResponse>())!;

        using var rejected = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(rejected, 409, "hold_expired");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var old = await db.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == setup.Hold.Id);
        var current = await db.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == successor.Id);
        Assert.Equal(SeatHoldStatus.Expired, old.Status);
        Assert.Equal(setup.Hold.ExpiresAt, old.ExpiresAt);
        Assert.Equal(setup.Hold.TotalAmount, old.TotalAmount.Amount);
        Assert.All(old.Items, item => Assert.Equal(1, item.AllocationVersion));
        Assert.Equal(SeatHoldStatus.Active, current.Status);
        Assert.Equal(successor.ExpiresAt, current.ExpiresAt);
        Assert.All(current.Items, item => Assert.Equal(3, item.AllocationVersion));
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(3, seat.Version);
        });
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Empty(await db.BookingItems.ToArrayAsync());
    }

    [PostgresFact]
    public async Task BookingRegression_RepeatedConversionPreservesTheSingleBookingAndItsDeadline()
    {
        var setup = await OwnedHold();
        var original = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        foreach (var at in new[] { original.CreatedAt.AddMinutes(1), setup.Hold.ExpiresAt.AddMinutes(1) })
        {
            clock.Set(at);
            using var repeated = await ConvertHold(setup.Hold.Id, setup.Owner);
            await AssertProblem(repeated, 409, "hold_not_active");
            using var read = await AdminRequest(HttpMethod.Get, $"/api/bookings/{original.Id}", setup.Owner);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var history = (await read.Content.ReadFromJsonAsync<BookingResponse>())!;
            Assert.Equal(original with { Items = history.Items }, history);
            Assert.Equal(original.Items, history.Items);
            await AssertBookingAllocation(original, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var stored = await db.Bookings.SingleAsync();
            Assert.Equal(original.Id, stored.Id);
            Assert.Equal(setup.Hold.Id, stored.SourceHoldId);
            Assert.Equal(setup.OwnerId, stored.UserId);
            Assert.Equal(2, await db.BookingItems.CountAsync());
            Assert.Equal(SeatHoldStatus.Converted, (await db.SeatHolds.SingleAsync()).Status);
        }
    }

    [PostgresFact]
    public async Task BookingRegression_ConversionAndCatalogueChangesPreserveIndependentPriceSnapshots()
    {
        var (token, session) = await HoldSession();
        Guid[] ids;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var inventory = await db.SessionSeats.OrderBy(value => value.Id).ToArrayAsync();
            ids = inventory.Select(value => value.Id).ToArray();
            for (var index = 0; index < inventory.Length; index++)
            {
                db.Entry(inventory[index]).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = index == 0 ? 12.50m : 7.25m;
                db.Entry(inventory[index]).ComplexProperty(value => value.Price).Property(value => value.Currency).CurrentValue = "EUR";
            }
            await db.SaveChangesAsync();
        }
        using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token, new CreateHoldRequest(ids));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var hold = (await created.Content.ReadFromJsonAsync<HoldResponse>())!;
        var booking = await CreateBookingForRead(hold.Id, token);
        Assert.Equal(19.75m, booking.TotalAmount);
        Assert.Equal("EUR", booking.Currency);
        Assert.Equal(hold.Items.OrderBy(value => value.SessionSeatId).Select(value => (value.SessionSeatId, value.Price, value.Currency)),
            booking.Items.Select(value => (value.SessionSeatId, value.Price, value.Currency)));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            // Test-only persistence mutation: allocated-price editing is not an exposed workflow.
            foreach (var seat in await db.SessionSeats.ToArrayAsync())
            {
                db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = 333.33m;
                db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Currency).CurrentValue = "USD";
            }
            await db.SaveChangesAsync();
        }
        using var holdRead = await AdminRequest(HttpMethod.Get, $"/api/holds/{hold.Id}", token);
        using var bookingRead = await AdminRequest(HttpMethod.Get, $"/api/bookings/{booking.Id}", token);
        Assert.Equal(HttpStatusCode.OK, holdRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bookingRead.StatusCode);
        var heldHistory = (await holdRead.Content.ReadFromJsonAsync<HoldResponse>())!;
        var bookingHistory = (await bookingRead.Content.ReadFromJsonAsync<BookingResponse>())!;
        Assert.Equal(hold with { Status = (int)SeatHoldStatus.Converted, Items = heldHistory.Items }, heldHistory);
        Assert.Equal(hold.Items.OrderBy(value => value.SessionSeatId), heldHistory.Items.OrderBy(value => value.SessionSeatId));
        Assert.Equal(booking with { Items = bookingHistory.Items }, bookingHistory);
        Assert.Equal(booking.Items, bookingHistory.Items);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.All(await database.SessionSeats.ToArrayAsync(), seat => Assert.Equal(new Money(333.33m, "USD"), seat.Price));
        Assert.All(await database.SeatHoldItems.ToArrayAsync(), item => Assert.Equal(2, item.AllocationVersion));
        Assert.All(await database.BookingItems.ToArrayAsync(), item => Assert.Equal(3, item.AllocationVersion));
    }

    [PostgresFact]
    public async Task BookingRegression_UnpaidBookingExpiresAtItsCapturedConfiguredDeadline()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BookingTiming:PaymentCompletionWindow"] = "00:07:00"
        }).Build();
        using var timings = new ServiceCollection().AddBookingTiming(configuration).BuildServiceProvider();
        var policy = timings.GetRequiredService<BookingTimingPolicy>();
        clock.Set(clock.GetUtcNow().AddMinutes(8));
        Guid bookingId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var caller = new BookingRegressionUser(setup.OwnerId);
            var conversion = new HoldConversionService(db, new SessionSeatAllocationWriter(db),
                new ResourceOwnershipService(caller), caller, clock, policy, app.Services.GetRequiredService<IHoldCache>());
            bookingId = Guid.NewGuid();
            Assert.NotNull(await conversion.ConvertAsync(setup.Hold.Id, bookingId, "BK-CONFIGURED"));
        }
        using var initial = await AdminRequest(HttpMethod.Get, $"/api/bookings/{bookingId}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var booking = (await initial.Content.ReadFromJsonAsync<BookingResponse>())!;
        Assert.Equal(booking.CreatedAt.AddMinutes(7), booking.PaymentDeadline);
        Assert.True(booking.PaymentDeadline > setup.Hold.ExpiresAt);
        // The expiry host has a different startup policy; it must honor the stored cutoff.
        Assert.Equal(TimeSpan.FromMinutes(15), app.Services.GetRequiredService<BookingTimingPolicy>().PaymentCompletionWindow);
        clock.Set(setup.Hold.ExpiresAt);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        clock.Set(booking.PaymentDeadline.AddTicks(-1));
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
        clock.Set(booking.PaymentDeadline);
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(booking, BookingStatus.Expired, SessionSeatStatus.Available, 3);
        using var login = await Login("owner@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        using var details = await AdminRequest(HttpMethod.Get, $"/api/bookings/{booking.Id}", token);
        var history = (await details.Content.ReadFromJsonAsync<BookingResponse>())!;
        Assert.Equal((int)BookingStatus.Expired, history.Status);
        Assert.Equal(booking.PaymentDeadline, history.PaymentDeadline);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await database.Payments.ToArrayAsync());
        Assert.Equal(SeatHoldStatus.Converted, (await database.SeatHolds.SingleAsync()).Status);
    }

    private sealed class BookingRegressionUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
    }
}
