using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TicketBooking.Api.Validation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.IntegrationTests;

public class CatalogueValidatorTests
{
    [Fact]
    public void SeatBatchValidation_ReportsIndexedFieldsAndDoesNotRewriteInput()
    {
        var input = new AddSeatsRequest([new(" A ", 1), null, new(" ", 0, 999)]);
        var result = new AddSeatsRequestValidator(new SeatRequestValidator()).Validate(input);
        Assert.Equal(new[] { "seats[1]", "seats[2].number", "seats[2].row", "seats[2].type" }, result.Errors.Select(e => e.PropertyName).Order());
        Assert.Equal(" A ", input.Seats![0]!.Row);
    }

    [Fact]
    public void PartialEventValidation_AllowsOmittedFieldsButRejectsEmptyPatchAndInvalidSuppliedFields()
    {
        var validator = new UpdateEventRequestValidator();
        Assert.True(validator.Validate(new UpdateEventRequest(Status: 1)).IsValid);
        Assert.True(validator.Validate(new UpdateEventRequest(Description: "")).IsValid);
        Assert.Equal("event", Assert.Single(validator.Validate(new UpdateEventRequest()).Errors).PropertyName);
        var result = validator.Validate(new UpdateEventRequest(Title: " ", DurationMinutes: 0, Status: 999));
        Assert.Equal(new[] { "durationMinutes", "status", "title" }, result.Errors.Select(e => e.PropertyName).Order());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(10, true)]
    public void SessionValidation_UsesInjectedClockAndPostgresPrecision(long ticks, bool valid)
    {
        var clock = new ValidationClock();
        var validator = new CreateSessionRequestValidator(clock);
        var request = new CreateSessionRequest(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddTicks(ticks), 0m, "UAH");
        var result = validator.Validate(request);
        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal("startsAt", Assert.Single(result.Errors).PropertyName);
        if (valid)
        {
            clock.Now = request.StartsAt!.Value;
            Assert.False(validator.Validate(request).IsValid);
        }
    }

    [Fact]
    public void QueryValidation_CollectsIndependentErrorsAndRejectsRepeatedParameters()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["page"] = "2147483647",
            ["pageSize"] = "100",
            ["date"] = "2030-02-30",
            ["venueId"] = "wrong",
            ["query"] = new[] { "one", "two" }
        });
        var result = new CatalogueQueryValidator().Validate(CatalogueQuery.Bind(query, true));
        Assert.Equal(new[] { "date", "page", "query", "venueId" }, result.Errors.Select(e => e.PropertyName).Order());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("+1", false)]
    [InlineData("20", true)]
    [InlineData("100", true)]
    public void QueryValidation_BoundsPageSizeWithoutClamping(string size, bool valid)
    {
        var request = new CatalogueQuery(null, null, null, null, size, []);
        var result = new CatalogueQueryValidator().Validate(request);
        Assert.Equal(valid, result.IsValid);
        if (valid) Assert.Equal(int.Parse(size), request.Parse().PageSize);
        else Assert.Equal("pageSize", Assert.Single(result.Errors).PropertyName);
    }

    private sealed class ValidationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
