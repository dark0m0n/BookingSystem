namespace TicketBooking.Domain;

internal static class DomainGuard
{
    public static Guid Id(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier must not be empty.", parameterName);
        }

        return value;
    }

    public static string Text(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    public static T Defined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be defined.");
        }

        return value;
    }
}
