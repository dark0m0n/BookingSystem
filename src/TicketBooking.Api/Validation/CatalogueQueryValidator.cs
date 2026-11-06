using System.Globalization;
using FluentValidation;

namespace TicketBooking.Api.Validation;

public sealed class CatalogueQueryValidator : AbstractValidator<CatalogueQuery>
{
    public CatalogueQueryValidator()
    {
        RuleFor(x => x.Query).Must(x => x is null || x.Trim().Length <= 200).WithMessage("Query must contain at most 200 characters.").OverridePropertyName("query");
        RuleFor(x => x.Date).Must(x => x is null || DateOnly.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("Date must be a valid yyyy-MM-dd date in UTC.").OverridePropertyName("date");
        RuleFor(x => x.VenueId).Must(x => x is null || Guid.TryParse(x, out var id) && id != Guid.Empty)
            .WithMessage("Venue ID must be a nonempty GUID.").OverridePropertyName("venueId");
        RuleFor(x => x.Page).Must(x => Page(x, int.MaxValue)).WithMessage("Page must be a positive Int32 integer.").OverridePropertyName("page");
        RuleFor(x => x.PageSize).Must(x => Page(x, 100)).WithMessage("Page size must be between 1 and 100.").OverridePropertyName("pageSize");
        RuleFor(x => x).Must(x => ((long)(x.Page is null ? 1 : int.Parse(x.Page, CultureInfo.InvariantCulture)) - 1) *
            (x.PageSize is null ? 20 : int.Parse(x.PageSize, CultureInfo.InvariantCulture)) <= int.MaxValue)
            .When(x => Page(x.Page, int.MaxValue) && Page(x.PageSize, 100))
            .WithMessage("Page offset exceeds the supported range.").OverridePropertyName("page");
        RuleFor(x => x).Custom((input, context) =>
        {
            foreach (var name in input.RepeatedParameters) context.AddFailure(name, "Supply this parameter only once.");
        });
    }

    private static bool Page(string? value, int maximum) => value is null ||
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= maximum;
}
