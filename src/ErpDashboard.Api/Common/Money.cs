namespace ErpDashboard.Api.Common;

public static class Money
{
    public static long ToMinorUnits(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        var minor = amount * 100m;
        if (minor != decimal.Truncate(minor))
            throw new ArgumentException($"Amount {amount} has more than two decimal places.", nameof(amount));

        return checked((long)minor);
    }

    public static decimal FromMinorUnits(long minor) => minor / 100m;
}