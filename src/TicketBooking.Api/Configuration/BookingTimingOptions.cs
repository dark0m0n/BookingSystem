namespace TicketBooking.Api.Configuration;

public sealed class BookingTimingOptions
{
    public const string SectionName = "BookingTiming";

    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PaymentCompletionWindow { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CancellationLeadTime { get; set; } = TimeSpan.FromHours(1);
}
