using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketBooking.Api.Auth;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

public class DevelopmentAdminCommandTests
{
    private const string Email = "development-admin@example.test";
    private const string Password = "Test-only-Admin-Password1!";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task CommandLine_OutsideDevelopmentExitsWithFailure(string environment)
    {
        var result = await RunCommandLine(environment);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("only in Development", result.Error);
        Assert.DoesNotContain(Email, result.Output + result.Error);
        Assert.DoesNotContain(Password, result.Output + result.Error);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task OutsideDevelopment_RefusesBeforeResolvingPersistence(string environment)
    {
        var builder = CreateBuilder(environment);
        builder.Services.AddScoped<TicketBookingDbContext>(_ => throw new InvalidOperationException("Persistence must not be resolved."));
        var serviceCount = builder.Services.Count;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(builder, output, error);

        Assert.Equal(1, result);
        Assert.Equal(serviceCount, builder.Services.Count);
        Assert.Contains("only in Development", error.ToString());
        Assert.Empty(output.ToString());
        Assert.DoesNotContain(Email, error.ToString());
        Assert.DoesNotContain(Password, error.ToString());
    }

    [Theory]
    [InlineData("DevelopmentAdmin:Email", null)]
    [InlineData("DevelopmentAdmin:Email", "invalid-email")]
    [InlineData("DevelopmentAdmin:Password", null)]
    [InlineData("DevelopmentAdmin:Password", "")]
    [InlineData("DevelopmentAdmin:DisplayName", " ")]
    public async Task MissingOrMalformedConfiguration_FailsBeforeBuildingTheHost(string key, string? value)
    {
        var builder = CreateBuilder("Development");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value });
        var serviceCount = builder.Services.Count;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await DevelopmentAdminCommand.RunAsync(builder, output, error);

        Assert.Equal(1, result);
        Assert.Equal(serviceCount, builder.Services.Count);
        Assert.Empty(output.ToString());
        Assert.DoesNotContain(Email, error.ToString());
        Assert.DoesNotContain(Password, error.ToString());
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunCommandLine(
        string environment, string? connectionString = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(DevelopmentAdminCommand).Assembly.Location);
        start.ArgumentList.Add(DevelopmentAdminCommand.Switch);
        start.ArgumentList.Add("--environment");
        start.ArgumentList.Add(environment);
        start.Environment["DevelopmentAdmin__Email"] = Email;
        start.Environment["DevelopmentAdmin__Password"] = Password;
        start.Environment["DevelopmentAdmin__DisplayName"] = "Development Admin";
        start.Environment["ConnectionStrings__DefaultConnection"] = connectionString ?? "invalid-unused-connection";
        start.Environment["Logging__LogLevel__Default"] = "None";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            // A bootstrap command must finish without starting the long-running HTTP host.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    internal static WebApplicationBuilder CreateBuilder(string environment, string? connectionString = null,
        string email = Email, string password = Password)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(DevelopmentAdminCommand).Assembly.GetName().Name,
            Args = []
        });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DevelopmentAdmin:Email"] = email,
            ["DevelopmentAdmin:Password"] = password,
            ["ConnectionStrings:DefaultConnection"] = connectionString
        });
        return builder;
    }
}
