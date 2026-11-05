using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TicketBooking.Api.Auth;

namespace TicketBooking.IntegrationTests;

public class HttpCurrentUserTests
{
    [Fact]
    public void AuthenticatedGuidSubject_IsTheOnlySourceOfCallerId()
    {
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", id.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"))
        };
        context.Request.QueryString = new QueryString("?userId=" + Guid.NewGuid());

        Assert.Equal(id, new HttpCurrentUser(new HttpContextAccessor { HttpContext = context }).UserId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("anonymous")]
    [InlineData("duplicate-sub")]
    [InlineData("multiple-identities")]
    [InlineData("name-identifier-only")]
    public void UnauthenticatedInvalidOrAmbiguousSubjects_HaveNoCaller(string defect)
    {
        var id = Guid.NewGuid().ToString();
        var claims = new List<Claim>();
        if (defect is not ("missing" or "name-identifier-only"))
        {
            claims.Add(new Claim("sub", defect switch { "malformed" => "not-a-guid", "empty" => Guid.Empty.ToString(), _ => id }));
        }
        claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        if (defect == "duplicate-sub") claims.Add(new Claim("sub", id));
        var identity = new ClaimsIdentity(claims, defect == "anonymous" ? null : "Bearer");
        var principal = new ClaimsPrincipal(identity);
        if (defect == "multiple-identities") principal.AddIdentity(new ClaimsIdentity([new Claim("sub", id)], "Other"));
        var context = new DefaultHttpContext { User = principal };

        Assert.Null(new HttpCurrentUser(new HttpContextAccessor { HttpContext = context }).UserId);
    }

    [Fact]
    public void OutsideHttpRequest_HasNoCaller()
    {
        Assert.Null(new HttpCurrentUser(new HttpContextAccessor()).UserId);
    }
}
