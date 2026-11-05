namespace TicketBooking.Domain.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private DateTimeOffset utcNow = initialTime.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
        utcNow = utcNow.Add(elapsed);
    }
}
