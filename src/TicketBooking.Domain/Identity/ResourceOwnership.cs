namespace TicketBooking.Domain.Identity;

public static class ResourceOwnership
{
    public static void EnsureOwner(Guid userId, Guid ownerId)
    {
        DomainGuard.Id(userId, nameof(userId));
        DomainGuard.Id(ownerId, nameof(ownerId));
        if (userId != ownerId)
        {
            throw new DomainRuleException(DomainError.ResourceNotOwned, "Only the resource owner can perform this operation.");
        }
    }
}
