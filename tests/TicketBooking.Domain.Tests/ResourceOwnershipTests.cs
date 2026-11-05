using TicketBooking.Domain.Identity;

namespace TicketBooking.Domain.Tests;

public class ResourceOwnershipTests
{
    [Fact]
    public void MatchingNonemptyIds_AreAllowed()
    {
        var id = Guid.NewGuid();

        ResourceOwnership.EnsureOwner(id, id);
    }

    [Fact]
    public void DifferentIds_ProduceTheStableOwnershipError()
    {
        var error = Assert.Throws<DomainRuleException>(() => ResourceOwnership.EnsureOwner(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(DomainError.ResourceNotOwned, error.Error);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyIds_AreNeverOwners(bool emptyUser, bool emptyOwner)
    {
        Assert.Throws<ArgumentException>(() => ResourceOwnership.EnsureOwner(
            emptyUser ? Guid.Empty : Guid.NewGuid(), emptyOwner ? Guid.Empty : Guid.NewGuid()));
    }
}
