using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Development;

public static class DevelopmentSampleDataCommand
{
    public const string Switch = "--seed-sample-data";

    public static async Task<int> RunAsync(WebApplicationBuilder builder, TextWriter output, TextWriter error,
        CancellationToken ct = default)
    {
        if (!builder.Environment.IsDevelopment()) return Fail(error, "Sample data seeding is available only in Development.");
        var connection = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection)) return Fail(error, "Configure ConnectionStrings:DefaultConnection before seeding sample data.");
        try
        {
            builder.Services.AddDbContext<TicketBookingDbContext>(options => options.UseNpgsql(connection));
            builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddScoped<DevelopmentSampleDataSeeder>();
            await using var app = builder.Build();
            await using var scope = app.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<DevelopmentSampleDataSeeder>().SeedAsync(ct);
            output.WriteLine(result.CreatedSession ? "Development sample data seeded." : "Development sample data already exists; records are unchanged.");
            output.WriteLine($"Venue: {result.VenueId}; auditorium: {result.AuditoriumId}; movie/event: {result.EventId}.");
            output.WriteLine($"Session: {result.SessionId}; starts at {result.StartsAt:O}; seats: {result.SeatCount}.");
            return 0;
        }
        catch (Exception)
        {
            return Fail(error, "Sample data seeding failed; check the database connection, applied migrations and sample catalogue consistency.");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }
}
