using ErpDashboard.Api.Common;

namespace ErpDashboard.Api.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("0.01", 1)]
    [InlineData("19.99", 1999)]
    [InlineData("1140", 114000)]
    [InlineData("1026.00", 102600)]
    [InlineData("0", 0)]
    public void ToMinorUnits_IsExact(string amount, long expected)
    {
        Assert.Equal(expected, Money.ToMinorUnits(decimal.Parse(amount)));
    }

    [Theory]
    [InlineData("0.005")]
    [InlineData("10.001")]
    public void ToMinorUnits_RejectsMoreThanTwoDecimals(string amount)
    {
        Assert.Throws<ArgumentException>(() => { Money.ToMinorUnits(decimal.Parse(amount)); });
    }

    [Fact]
    public void ToMinorUnits_RejectsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { Money.ToMinorUnits(-1m); });
    }

    [Fact]
    public void RoundTrip_IsLossless_ForEveryPiasterUpTo10000Pounds()
    {
        for (long minor = 0; minor <= 1_000_000; minor += 7)
            Assert.Equal(minor, Money.ToMinorUnits(Money.FromMinorUnits(minor)));
    }
}