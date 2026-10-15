using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoicePdfGenerator
{
    byte[] Generate(InvoiceDetailsResponse invoice);
}

public sealed class InvoicePdfGenerator(IOptions<CompanySettings> options) : IInvoicePdfGenerator
{
    private readonly CompanySettings _company = options.Value;

    public byte[] Generate(InvoiceDetailsResponse inv) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            if (inv.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            {
                page.Foreground()
                    .AlignCenter().AlignMiddle()
                    .Rotate(-30)
                    .Text(inv.Status.ToString().ToUpperInvariant())
                    .FontSize(90).Bold().FontColor(Colors.Grey.Lighten3);
            }

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(_company.Name).FontSize(18).Bold();
                    col.Item().Text(_company.Address);
                    col.Item().Text($"Tax ID: {_company.TaxId}");
                });

                row.ConstantItem(200).AlignRight().Column(col =>
                {
                    col.Item().Text("INVOICE").FontSize(20).Bold();
                    col.Item().Text($"No: {ShortId(inv.Id)}");
                    col.Item().Text($"Date: {inv.InvoiceDate:yyyy-MM-dd}");
                    col.Item().Text($"Due: {inv.DueDate:yyyy-MM-dd}");
                    col.Item().Text($"Status: {inv.Status}").Bold();
                });
            });

            page.Content().PaddingVertical(20).Column(col =>
            {
                col.Spacing(15);

                col.Item().Text(t =>
                {
                    t.Span("Bill to: ").Bold();
                    t.Span(inv.CustomerName);
                });

                if (inv.OrderId is { } orderId)
                    col.Item().Text($"Order ref: {ShortId(orderId)}");

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4); 
                        c.RelativeColumn(1); 
                        c.RelativeColumn(2); 
                        c.RelativeColumn(1); 
                        c.RelativeColumn(2); 
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).Text("Description");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Tax %");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Total");
                    });

                    foreach (var l in inv.Lines)
                    {
                        table.Cell().Element(BodyCell).Text(l.Description);
                        table.Cell().Element(BodyCell).AlignRight().Text(l.Quantity.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(l.UnitPrice));
                        table.Cell().Element(BodyCell).AlignRight().Text(l.TaxRate.ToString("0.##", CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(l.LineTotal));
                    }
                });

                col.Item().AlignRight().Width(230).Column(totals =>
                {
                    TotalRow(totals, "Subtotal", inv.Subtotal);
                    TotalRow(totals, "Tax", inv.TaxAmount);
                    TotalRow(totals, "Total", inv.TotalAmount, bold: true);
                });

                if (inv.Status == InvoiceStatus.Paid && inv.PaidAt is { } paidAt)
                    col.Item().Text($"Paid on {paidAt:yyyy-MM-dd} via {inv.PaymentMethod}").Italic();
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();

    private void TotalRow(ColumnDescriptor col, string label, decimal value, bool bold = false) =>
        col.Item().BorderTop(bold ? 1 : 0).PaddingVertical(3).Row(r =>
        {
            var l = r.RelativeItem().Text(label);
            var v = r.RelativeItem().AlignRight().Text(Money(value));
            if (bold) { l.Bold(); v.Bold(); }
        });

    private string Money(decimal v) =>
        $"{v.ToString("N2", CultureInfo.InvariantCulture)} {_company.Currency}";

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).PaddingVertical(5).DefaultTextStyle(x => x.Bold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
}