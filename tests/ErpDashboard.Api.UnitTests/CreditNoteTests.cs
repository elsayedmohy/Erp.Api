using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Features.Finance.Invoices;
using ErpDashboard.Api.Features.Finance.Invoices.CreditNotes;

namespace ErpDashboard.Api.UnitTests;

public class CreditNoteTests
{
    private static readonly InvoiceLine Sofa = new()
        { Id = Guid.NewGuid(), Description = "Sofa", Quantity = 2, UnitPrice = 100m, Discount = 15m, TaxRate = 14m };
    private static readonly InvoiceLine Delivery = new()
        { Id = Guid.NewGuid(), Description = "Delivery", Quantity = 1, UnitPrice = 50m, TaxRate = 0m };

    
    private static List<CreditNoteLine> Build(Dictionary<Guid, decimal>? credited, params CreditLineInput[] lines) =>
        CreditNoteFactory.BuildLines([Sofa, Delivery], credited ?? [], lines);
    
    [Fact]
    public void Tax_UsesOriginalLineRate_AndSameRounding()
    {
        var lines = Build(null, new(Sofa.Id, 185m), new(Delivery.Id, 50m));

        Assert.Equal(25.90m, CreditNoteFactory.LineTax(lines[0])); 
        Assert.Equal(0m, CreditNoteFactory.LineTax(lines[1]));     
        Assert.Equal(260.90m, CreditNoteFactory.Total(lines));
    }

    [Fact]
    public void FullCredit_ReversesTheInvoiceExactly()
    {
        var lines = Build(null, new(Sofa.Id, InvoiceFactory.LineNet(Sofa)), new(Delivery.Id, InvoiceFactory.LineNet(Delivery)));

        Assert.Equal(InvoiceFactory.CalculateTotal([Sofa, Delivery]), CreditNoteFactory.Total(lines));
    }

    [Theory]
    [InlineData(0, 185.01)]   
    [InlineData(100, 85.01)]  
    public void Credit_AboveRemaining_IsRejected(decimal earlier, decimal requested)
    {
        var credited = new Dictionary<Guid, decimal> { [Sofa.Id] = earlier };
        var ex = Assert.Throws<RequestValidationException>(() => { Build(credited, new CreditLineInput(Sofa.Id, requested)); });
        Assert.Contains("lines[0].netAmount", ex.Errors.Keys);
    }

    [Fact]
    public void Credit_ExactlyRemaining_IsAllowed()
    {
        var credited = new Dictionary<Guid, decimal> { [Sofa.Id] = 100m };

        Assert.Single(Build(credited, new CreditLineInput(Sofa.Id, 85m)));
    }

    [Fact]
    public void LineFromAnotherInvoice_IsRejected()
    {

        Assert.Throws<RequestValidationException>(() => { Build(null, new CreditLineInput(Guid.NewGuid(), 10m)); });    }

    [Fact]
    public void SameLineTwice_IsRejected()
    {
        Assert.Throws<RequestValidationException>(() => Build(null, new(Sofa.Id, 10m), new(Sofa.Id, 10m)));
    }

    [Theory]
    [InlineData(500, 0, InvoicePaymentStatus.PartiallyPaid)]
    [InlineData(500, 640, InvoicePaymentStatus.Paid)]   // paid + credited covers the total
    [InlineData(1140, 100, InvoicePaymentStatus.Paid)]  // over-covered: refund due
    public void PaymentStatus_CountsCredits(decimal paid, decimal credited, InvoicePaymentStatus expected)
    {
        var balance = new InvoiceBalance
        {
            Invoice = new Invoice { Status = InvoiceStatus.Issued, TotalAmount = 1140m, DueDate = new DateOnly(2026, 12, 31) },
            Paid = paid,
            Credited = credited
        };

        Assert.Equal(expected, InvoicePaymentRules.StatusOf(balance, new DateOnly(2026, 12, 1)));
    }
}