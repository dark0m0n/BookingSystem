using TicketBooking.Contracts.Administration;
using TicketBooking.Domain;

namespace TicketBooking.Api.Validation;

internal static class RequestRules
{
    public static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum;
    public static bool Future(DateTimeOffset? value, TimeProvider clock) => value is { } instant &&
        new DateTimeOffset(instant.UtcTicks / 10 * 10, TimeSpan.Zero) > clock.GetUtcNow();
    public static bool Price(CreateSessionRequest request)
    {
        try { _ = new Money(request.Price ?? throw new ArgumentException(), request.Currency!); return true; }
        catch (ArgumentException) { return false; }
    }
}
