using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Domain;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

internal static class MoneyMapping
{
    public static void ConfigureMoney(this ComplexPropertyBuilder<Money> builder, string amountColumn, string currencyColumn)
    {
        builder.IsRequired();
        builder.Property(money => money.Amount).HasColumnName(amountColumn).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(money => money.Currency).HasColumnName(currencyColumn).HasMaxLength(3).IsRequired();
    }
}
