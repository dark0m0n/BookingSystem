using System.Globalization;
using FluentValidation;

namespace TicketBooking.Api.Validation;

public sealed record BookingListQuery(string? Page, string? PageSize, IReadOnlyList<string> RepeatedParameters)
{
    public static BookingListQuery Bind(IQueryCollection query)
    {
        var repeated = new List<string>();
        string? Read(string name)
        {
            if (!query.TryGetValue(name, out var values)) return null;
            if (values.Count == 1) return values[0];
            repeated.Add(name);
            return null;
        }
        return new BookingListQuery(Read("page"), Read("pageSize"), repeated);
    }

    public (int Page, int PageSize, int Offset) Parse()
    {
        var page = Page is null ? 1 : int.Parse(Page, CultureInfo.InvariantCulture);
        var pageSize = PageSize is null ? 20 : int.Parse(PageSize, CultureInfo.InvariantCulture);
        return (page, pageSize, (int)(((long)page - 1) * pageSize));
    }
}

public sealed class BookingListQueryValidator : AbstractValidator<BookingListQuery>
{
    public BookingListQueryValidator()
    {
        RuleFor(input => input.Page).Must(value => ValidPage(value, int.MaxValue))
            .WithMessage("Page must be a positive Int32 integer.").OverridePropertyName("page");
        RuleFor(input => input.PageSize).Must(value => ValidPage(value, 100))
            .WithMessage("Page size must be between 1 and 100.").OverridePropertyName("pageSize");
        RuleFor(input => input).Must(input =>
            ((long)(input.Page is null ? 1 : int.Parse(input.Page, CultureInfo.InvariantCulture)) - 1) *
            (input.PageSize is null ? 20 : int.Parse(input.PageSize, CultureInfo.InvariantCulture)) <= int.MaxValue)
            .When(input => ValidPage(input.Page, int.MaxValue) && ValidPage(input.PageSize, 100))
            .WithMessage("Page offset exceeds the supported range.").OverridePropertyName("page");
        RuleFor(input => input).Custom((input, context) =>
        {
            foreach (var name in input.RepeatedParameters) context.AddFailure(name, "Supply this parameter only once.");
        });
    }

    private static bool ValidPage(string? value, int maximum) => value is null ||
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= maximum;
}
