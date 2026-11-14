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

            var watermark = inv.Status switch
            {
                InvoiceStatus.Draft => "DRAFT",
                InvoiceStatus.Cancelled => "CANCELLED",
                _ when inv.PaymentStatus == InvoicePaymentStatus.Paid => "PAID",
                _ => null
            };

            if (watermark is not null)
            {
                page.Foreground()
                    .AlignCenter().AlignMiddle()
                    .Rotate(-30)
                    .Text(watermark)
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
                    col.Item().Text($"No: {inv.Number ?? "DRAFT"}");
                    col.Item().Text($"Date: {inv.InvoiceDate:yyyy-MM-dd}");
                    col.Item().Text($"Due: {inv.DueDate:yyyy-MM-dd}");
                    col.Item().Text($"Status: {inv.PaymentStatus?.ToString() ?? inv.Status.ToString()}").Bold();
                });
            });

            page.Content().PaddingVertical(20).Column(col =>
            {
                col.Spacing(15);

                col.Item().Text(t =>
                {
                    t.Span("Bill to: ").Bold();
                    t.Span(inv.ShowroomName);
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
                        c.RelativeColumn(2); 
                        c.RelativeColumn(1); 
                        c.RelativeColumn(2); 
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).Text("Description");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Discount");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Tax %");
                        h.Cell().Element(HeaderCell).AlignRight().Text("Total");
                    });

                    foreach (var l in inv.Lines)
                    {
                        table.Cell().Element(BodyCell).Text(l.Description);
                        table.Cell().Element(BodyCell).AlignRight().Text(l.Quantity.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(l.UnitPrice));
                        table.Cell().Element(BodyCell).AlignRight().Text(l.Discount == 0 ? "-" : Money(l.Discount));
                        table.Cell().Element(BodyCell).AlignRight().Text(l.TaxRate.ToString("0.##", CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(l.LineTotal));
                    }
                });

                col.Item().AlignRight().Width(230).Column(totals =>
                {
                    TotalRow(totals, "Subtotal", inv.Subtotal);
                    if (inv.DiscountTotal > 0)
                        TotalRow(totals, "Discount", -inv.DiscountTotal);
                    TotalRow(totals, "Tax (after discount)", inv.TaxAmount);
                    TotalRow(totals, "Total", inv.TotalAmount, bold: true);
                    if (inv.Payments.Count > 0)
                    {
                        TotalRow(totals, "Paid", -inv.AmountPaid);
                        TotalRow(totals, "Balance due", inv.Balance, bold: true);
                    }
                });
                
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