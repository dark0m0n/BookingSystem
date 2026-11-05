using System.Globalization;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Domain.Tests;

public class MoneyTests
{
    public static IEnumerable<object[]> PriceBoundaries()
    {
        foreach (var currency in new[] { "UAH", "EUR", "USD" })
        {
            yield return [currency, new[] { 0m }, 0m];
            yield return [currency, new[] { 0.01m, 0.01m, 0.01m, 0.01m, 0.01m, 0.01m, 0.01m }, 0.07m];
            yield return [currency, new[] { Money.MaxAmount - 0.01m, 0.01m }, Money.MaxAmount];
        }
    }

    [Theory]
    [MemberData(nameof(PriceBoundaries))]
    public void PricePipeline_PreservesExactBoundaryTotalsAndIndependentSnapshots(string currency, decimal[] prices, decimal expectedAmount)
    {
        var sessionId = Guid.NewGuid();
        var seats = prices.Select(price => LifecycleTestData.Seat(sessionId, price, currency)).ToArray();
        var hold = new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), LifecycleTestData.SessionStartsAt,
            seats, LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-PRICE-BOUNDARY",
            LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "price-boundary", LifecycleTestData.Clock);
        var expected = new Money(expectedAmount, currency);

        Assert.Equal(expected, hold.TotalAmount);
        Assert.Equal(expected, booking.TotalAmount);
        Assert.Equal(expected, payment.Amount);
        Assert.Equal(seats.Length, hold.Items.Count);
        Assert.Equal(seats.Length, booking.Items.Count);
        for (var index = 0; index < seats.Length; index++)
        {
            Assert.Equal(new Money(prices[index], currency), hold.Items[index].PriceSnapshot);
            Assert.Equal(hold.Items[index].PriceSnapshot, booking.Items[index].PriceSnapshot);
            Assert.NotSame(seats[index].Price, hold.Items[index].PriceSnapshot);
            Assert.NotSame(hold.Items[index].PriceSnapshot, booking.Items[index].PriceSnapshot);
        }
        Assert.NotSame(hold.Items[0].PriceSnapshot, hold.TotalAmount);
        Assert.NotSame(booking.Items[0].PriceSnapshot, booking.TotalAmount);
        Assert.NotSame(hold.TotalAmount, booking.TotalAmount);
        Assert.NotSame(booking.TotalAmount, payment.Amount);
    }

    [Theory]
    [InlineData("UAH")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public void Money_CarriesAnExplicitCurrencyAndSupportsZero(string currency)
    {
        var money = new Money(0m, currency);

        Assert.Equal(0m, money.Amount);
        Assert.Equal(currency, money.Currency);
    }

    [Fact]
    public void Money_NormalizesCurrencyAndUsesValueEquality()
    {
        Assert.Equal(new Money(1.23m, "UAH"), new Money(1.2300m, " uah "));
        Assert.NotEqual(new Money(1m, "USD"), new Money(1m, "EUR"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("XYZ")]
    [InlineData("JPY")]
    public void Money_RejectsMissingOrUnsupportedCurrency(string? currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Money(1m, currency!));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("1.235")]
    [InlineData("10000000000000000.00")]
    [InlineData("79228162514264337593543950335")]
    public void Money_RejectsAmountsOutsideItsRangeOrScale(string value)
    {
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(amount, "UAH"));
    }

    [Fact]
    public void Sum_IsExactAndEnforcesTheRangeOfTheTotal()
    {
        Assert.Equal(new Money(0.30m, "EUR"), Money.Sum([new Money(0.10m, "EUR"), new Money(0.20m, "EUR")]));
        Assert.Equal(Money.MaxAmount, new Money(Money.MaxAmount, "UAH").Amount);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Money.Sum([new Money(Money.MaxAmount, "UAH"), new Money(0.01m, "UAH")]));
    }

    [Fact]
    public void Sum_RejectsMixedCurrenciesEvenForZeroAmounts()
    {
        Assert.Throws<ArgumentException>(() => Money.Sum([new Money(0m, "USD"), new Money(0m, "EUR")]));
    }

    [Fact]
    public void Sum_RejectsAnEmptyOrMissingCollectionAndNullValues()
    {
        Assert.Throws<ArgumentException>(() => Money.Sum([]));
        Assert.Throws<ArgumentNullException>(() => Money.Sum(null!));
        Assert.Throws<ArgumentException>(() => Money.Sum([null!]));
        Assert.Throws<ArgumentNullException>(() => Money.Sum([new Money(1m, "UAH"), null!]));
    }

    [Theory]
    [InlineData("UAH")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public void PricePipeline_DerivesHoldBookingAndPaymentTotalsFromInventory(string currency)
    {
        var sessionId = Guid.NewGuid();
        var seats = new List<SessionSeat>
        {
            LifecycleTestData.Seat(sessionId, 125.55m, currency),
            LifecycleTestData.Seat(sessionId, 35.45m, currency)
        };
        var hold = new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(10), seats, LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);
        var firstSeatId = seats[0].Id;
        // Replacing the current inventory representation must not change historical snapshots.
        seats[0] = new SessionSeat(firstSeatId, sessionId, seats[0].SeatId, new Money(200m, currency));

        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-MONEY", LifecycleTestData.Clock, LifecycleTestData.SessionStartsAt, LifecycleTestData.TimingPolicy);
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "payment-request", LifecycleTestData.Clock);

        Assert.Equal(new Money(125.55m, currency), hold.Items[0].PriceSnapshot);
        Assert.Equal(firstSeatId, hold.Items[0].SessionSeatId);
        Assert.All(hold.Items, item => Assert.Equal(hold.Id, item.SeatHoldId));
        Assert.Equal(new Money(161m, currency), hold.TotalAmount);
        Assert.Equal(hold.TotalAmount, booking.TotalAmount);
        Assert.Equal(booking.TotalAmount, payment.Amount);
        Assert.Equal(booking.Id, payment.BookingId);
    }

    [Fact]
    public void Hold_RejectsMixedCurrenciesAndOutOfRangeTotals()
    {
        var sessionId = Guid.NewGuid();
        SeatHold Hold(params SessionSeat[] seats) => new(Guid.NewGuid(), sessionId, Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(10), seats, LifecycleTestData.Clock, LifecycleTestData.TimingPolicy);

        Assert.Throws<ArgumentException>(() => Hold(LifecycleTestData.Seat(sessionId, 1m, "UAH"),
            LifecycleTestData.Seat(sessionId, 1m, "USD")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hold(LifecycleTestData.Seat(sessionId, Money.MaxAmount),
            LifecycleTestData.Seat(sessionId, 0.01m)));
    }

    [Theory]
    [InlineData(SessionSeatStatus.Held)]
    [InlineData(SessionSeatStatus.Booked)]
    [InlineData(SessionSeatStatus.Unavailable)]
    public void Hold_RejectsInventoryThatIsNotAvailable(SessionSeatStatus status)
    {
        var sessionId = Guid.NewGuid();

        Assert.Throws<DomainRuleException>(() => new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), LifecycleTestData.Now.AddMinutes(10), [LifecycleTestData.Seat(sessionId, 1m, status: status)], LifecycleTestData.Clock, LifecycleTestData.TimingPolicy));
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    public void Payment_CannotStartForANonPendingBooking(BookingStatus status)
    {
        var booking = LifecycleTestData.Booking();
        switch (status)
        {
            case BookingStatus.Confirmed:
                booking.Confirm(LifecycleTestData.Clock);
                break;
            case BookingStatus.Cancelled:
                booking.Cancel(booking.UserId, LifecycleTestData.Clock);
                break;
            case BookingStatus.Expired:
                booking.Expire(LifecycleTestData.At(booking.PaymentDeadline));
                break;
        }

        Assert.Throws<DomainRuleException>(() => PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "key", LifecycleTestData.Clock));
    }
}
