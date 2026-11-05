namespace TicketBooking.Domain;

/// <summary>An expected business-rule rejection, independent of HTTP or public message wording.</summary>
public sealed class DomainRuleException(DomainError error, string message) : InvalidOperationException(message)
{
    public DomainError Error { get; } = error;
}
