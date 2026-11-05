using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Configuration;
using TicketBooking.Api.Errors;
using TicketBooking.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddBookingTiming(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddApiErrors();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing.");

builder.Services.AddDbContext<TicketBookingDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<SessionSeatAllocationWriter>();

var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("JWT signing key is missing.");

var app = builder.Build();

// Use the same safe error format in every environment.
app.UseExceptionHandler();
app.UseStatusCodePages(ApiProblemDetails.WriteStatusCodeAsync);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Liveness only: database readiness checks can be added separately.
app.MapHealthChecks("/health");

app.Run();
