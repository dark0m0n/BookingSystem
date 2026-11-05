using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Configuration;
using TicketBooking.Api.Errors;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;

var initializeAdmin = args.Contains(DevelopmentAdminCommand.Switch, StringComparer.Ordinal);
var applicationArgs = args.Where(argument => argument != DevelopmentAdminCommand.Switch).ToArray();
var builder = WebApplication.CreateBuilder(applicationArgs);

if (initializeAdmin)
{
    Environment.ExitCode = await DevelopmentAdminCommand.RunAsync(builder, Console.Out, Console.Error);
    return;
}

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
builder.Services.AddTicketBookingIdentity().AddSignInManager();
builder.Services.AddTicketBookingAuthentication(builder.Configuration);

var app = builder.Build();

// Use the same safe error format in every environment.
app.UseExceptionHandler();
app.UseStatusCodePages(ApiProblemDetails.WriteStatusCodeAsync);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();

// Liveness only: database readiness checks can be added separately.
app.MapHealthChecks("/health");

app.Run();
