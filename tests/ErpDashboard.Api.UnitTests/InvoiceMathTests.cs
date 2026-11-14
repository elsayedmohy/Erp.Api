using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Features.Finance.Invoices;

namespace ErpDashboard.Api.UnitTests;

public class InvoiceMathTests
{
    private static InvoiceLine Line(int qty, decimal price, decimal discount = 0m, decimal rate = 14m) =>
        InvoiceFactory.BuildLines([new InvoiceLineInput(null, "Item", qty, price, rate, discount)]).Single();

    [Fact]
    public void Tax_IsCalculatedAfterDiscount()
    {
        var line = Line(qty: 1, price: 1000m, discount: 100m);

        Assert.Equal(900m, InvoiceFactory.LineNet(line));
        Assert.Equal(126m, InvoiceFactory.LineTax(line));
    }

    [Theory]
    [InlineData(3, 33.33, 0.03, 13.99)] // old split-line method gave 14.00
    [InlineData(3, 33.33, 0.00, 14.00)] // 99.99 × 14% = 13.9986
    [InlineData(1, 0.25, 0.00, 0.04)]   // 0.035 rounds away from zero
    [InlineData(1, 0.75, 0.00, 0.11)]   // 0.105 rounds away from zero
    [InlineData(2, 50.00, 100.00, 0.00)] // fully discounted line has no tax
    public void Tax_RoundsToNearestPiaster(int qty, decimal price, decimal discount, decimal expectedTax)
    {
        Assert.Equal(expectedTax, InvoiceFactory.LineTax(Line(qty, price, discount)));
    }

    [Fact]
    public void Total_EqualsSumOfLines_Exactly_ForManyRandomInvoices()
    {
        var random = new Random(20261114);

        for (var n = 0; n < 2_000; n++)
        {
            var inputs = Enumerable.Range(0, random.Next(1, 8)).Select(_ =>
            {
                var qty = random.Next(1, 50);
                var price = Math.Round((decimal)random.NextDouble() * 5_000m + 0.01m, 2);
                var gross = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero);
                var discount = Math.Round(gross * (decimal)random.NextDouble() * 0.3m, 2);
                return new InvoiceLineInput(null, "Item", qty, price, 14m, discount);
            }).ToList();

            var lines = InvoiceFactory.BuildLines(inputs);
            var total = InvoiceFactory.CalculateTotal(lines);
            var sumOfLines = lines.Sum(l => InvoiceFactory.LineNet(l) + InvoiceFactory.LineTax(l));

            Assert.Equal(sumOfLines, total);
            Assert.Equal(Math.Round(total, 2), total);
            Assert.All(lines, l => Assert.Equal(Math.Round(InvoiceFactory.LineTax(l), 2), InvoiceFactory.LineTax(l)));
        }
    }

    [Fact]
    public void Discount_GreaterThanLine_IsRejected()
    {
        var ex = Assert.Throws<RequestValidationException>(() => Line(qty: 1, price: 100m, discount: 100.01m));
        Assert.Contains("lines[0].discount", ex.Errors.Keys);
    }

    [Fact]
    public void Discount_WithThreeDecimals_IsRejected()
    {
        Assert.Throws<RequestValidationException>(() => Line(qty: 1, price: 100m, discount: 0.005m));
    }

    [Fact]
    public void OrderLines_BecomeOneInvoiceLineEach_WithTheirDiscount()
    {
        var orderLines = new[]
        {
            new OrderLine { ProductId = Guid.NewGuid(), ProductName = "Sofa", Quantity = 2, UnitPrice = 100m, Discount = 15m },
            new OrderLine { ProductId = Guid.NewGuid(), ProductName = "Antreh", Quantity = 1, UnitPrice = 300m, Discount = 0m }
        };

        var lines = InvoiceFactory.BuildLines(OrderInvoiceBuilder.Lines(orderLines, 14m));

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.True(l.UnitPrice > 0));
        var sofa = lines.Single(l => l.Description == "Sofa");
        Assert.Equal(185m, InvoiceFactory.LineNet(sofa));
        Assert.Equal(25.90m, InvoiceFactory.LineTax(sofa));
    }
}