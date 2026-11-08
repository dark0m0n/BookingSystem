namespace TicketBooking.Api.Reservation;

public sealed class HoldExpiryOptions
{
    public const string SectionName = "HoldExpiry";
    public int BatchSize { get; set; } = 100;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (BatchSize is < 1 or > 1000)
            throw new InvalidOperationException("HoldExpiry:BatchSize must be between 1 and 1000.");
        if (PollInterval < TimeSpan.FromSeconds(1) || PollInterval > TimeSpan.FromHours(1))
            throw new InvalidOperationException("HoldExpiry:PollInterval must be between one second and one hour.");
    }
}
