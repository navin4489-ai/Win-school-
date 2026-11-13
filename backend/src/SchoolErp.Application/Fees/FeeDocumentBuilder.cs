using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Fees;

/// <summary>Renders fee receipts and statements as PDF documents.</summary>
public static class FeeDocumentBuilder
{
    private const string Accent = "#0f2c4d";
    private const string Muted = "#5b7186";
    private const string Line = "#e3e9ef";
    private const string Overdue = "#a8342a";

    static FeeDocumentBuilder()
    {
        // Free community licence (organisations under USD 1M annual revenue).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static FeeDocumentDto BuildReceipt(School school, string schoolCode, Student student,
        string className, Payment payment, string installmentName, decimal penaltyTotal)
    {
        var lateFee = Math.Min(payment.Amount, penaltyTotal);

        var pdf = Render(school, "Fee Receipt", $"Receipt {payment.ReceiptNo}", "RECEIPT", column =>
        {
            KeyValues(column, new (string, string, bool)[]
            {
                ("Student", student.FullName, false),
                ("Admission number", student.AdmissionNo, false),
                ("Class", className, false),
                ("Receipt number", payment.ReceiptNo, false),
                ("Payment date", payment.PaidAtUtc.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC", false),
                ("Payment mode", payment.Mode.ToString(), false),
                ("Reference", payment.ReferenceNo ?? payment.GatewayPaymentId ?? "-", false),
                ("Installment", installmentName, false)
            });

            Section(column, "Breakdown");
            var breakdown = new List<(string, string, bool)>
            {
                ("Fee", Money(payment.Amount - lateFee), false),
                ("Late fee", Money(lateFee), false)
            };
            if (payment.RefundedAmount > 0) breakdown.Add(("Refunded", "- " + Money(payment.RefundedAmount), false));
            breakdown.Add(("Total paid", Money(payment.Amount), true));
            KeyValues(column, breakdown);

            Note(column, "This is a computer generated receipt and does not need a signature.");
        });

        return new FeeDocumentDto { FileName = $"Receipt-{payment.ReceiptNo}.pdf", Content = pdf };
    }

    public static FeeDocumentDto BuildStatement(School school, Student student, string className,
        StudentLedgerDto ledger)
    {
        var pdf = Render(school, "Fee Statement", student.FullName, "STATEMENT", column =>
        {
            var summary = new List<(string, string, bool)>
            {
                ("Admission number", student.AdmissionNo, false),
                ("Class", className, false),
                ("Total billed", Money(ledger.TotalBilled), false),
                ("Discount", "- " + Money(ledger.Discount), false)
            };
            if (ledger.PlatformFee > 0) summary.Add(("Platform fee (included)", Money(ledger.PlatformFee), false));
            summary.Add(("Total paid", Money(ledger.TotalPaid), false));
            summary.Add(("Late fee applied", Money(ledger.PenaltyTotal), false));
            summary.Add(("Outstanding balance", Money(ledger.Balance), true));
            KeyValues(column, summary);

            Section(column, "Installments");
            Grid(column,
                new[] { "Installment", "Due date", "Fee", "Late fee", "Paid", "Balance", "Status" },
                new[] { 3f, 2f, 1.6f, 1.4f, 1.6f, 1.6f, 2f },
                new[] { false, false, true, true, true, true, false },
                ledger.Installments.Select(i => (
                    new[]
                    {
                        i.Name, i.DueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                        Number(i.Amount), Number(i.PenaltyAmount), Number(i.PaidAmount), Number(i.Balance),
                        i.Status + (i.IsOverdue ? " (overdue)" : string.Empty)
                    },
                    i.IsOverdue)).ToList());

            Section(column, "Payments");
            if (ledger.Payments.Count == 0)
            {
                column.Item().Text("No payments yet.").FontSize(9).FontColor(Muted);
            }
            else
            {
                Grid(column,
                    new[] { "Date", "Receipt", "Mode", "Amount", "Status" },
                    new[] { 2f, 3.2f, 1.8f, 1.8f, 1.6f },
                    new[] { false, false, false, true, false },
                    ledger.Payments.Select(p => (
                        new[]
                        {
                            p.PaidAtUtc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), p.ReceiptNo,
                            p.Mode.ToString(), Number(p.Amount), p.Status.ToString()
                        },
                        false)).ToList());
            }

            Note(column, $"Generated on {DateTime.UtcNow:dd MMM yyyy, HH:mm} UTC. Amounts in Indian Rupees (Rs.). " +
                         "This is a computer generated statement.");
        });

        return new FeeDocumentDto { FileName = $"Fee-Statement-{student.AdmissionNo}.pdf", Content = pdf };
    }

    private static byte[] Render(School school, string heading, string subHeading, string tag,
        Action<ColumnDescriptor> body)
    {
        var schoolName = string.IsNullOrWhiteSpace(school.Name) ? "School" : school.Name;
        var address = string.Join(", ", new[]
        {
            school.AddressLine1, school.AddressLine2, school.City, school.State, school.PostalCode
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var contact = string.Join("  ·  ", new[] { school.Phone, school.Email }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return QuestPDF.Fluent.Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor("#16232f"));

                page.Header().PaddingBottom(14).BorderBottom(2).BorderColor(Accent).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(schoolName).FontSize(15).Bold().FontColor(Accent);
                        if (address.Length > 0) c.Item().Text(address).FontSize(9).FontColor(Muted);
                        if (contact.Length > 0) c.Item().Text(contact).FontSize(9).FontColor(Muted);
                        c.Item().PaddingTop(8).Text(heading).FontSize(18).Bold().FontColor(Accent);
                        c.Item().Text(subHeading).FontSize(10).FontColor(Muted);
                    });
                    row.ConstantItem(90).AlignRight().AlignTop()
                        .Background("#eef3f8").Padding(6).AlignCenter()
                        .Text(tag).FontSize(8).Bold().FontColor(Accent).LetterSpacing(0.08f);
                });

                page.Content().PaddingTop(16).Column(column =>
                {
                    column.Spacing(4);
                    body(column);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void KeyValues(ColumnDescriptor column, IEnumerable<(string Label, string Value, bool Strong)> rows)
    {
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(3); });
            foreach (var (label, value, strong) in rows)
            {
                table.Cell().BorderBottom(1).BorderColor("#edf1f5").PaddingVertical(5).Text(label).FontColor(Muted);
                var cell = table.Cell().BorderBottom(1).BorderColor("#edf1f5").PaddingVertical(5).Text(value);
                if (strong) cell.Bold().FontColor(Accent).FontSize(11);
            }
        });
    }

    private static void Grid(ColumnDescriptor column, string[] headers, float[] widths, bool[] numeric,
        IReadOnlyList<(string[] Cells, bool Highlight)> rows)
    {
        column.Item().Border(1).BorderColor(Line).Table(table =>
        {
            table.ColumnsDefinition(c => { foreach (var w in widths) c.RelativeColumn(w); });

            table.Header(header =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = header.Cell().Background("#f2f6fa").Padding(6);
                    (numeric[i] ? cell.AlignRight() : cell).Text(headers[i].ToUpperInvariant())
                        .FontSize(7.5f).Bold().FontColor(Muted);
                }
            });

            foreach (var (cells, highlight) in rows)
            {
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = table.Cell().BorderTop(1).BorderColor("#edf1f5").Padding(6);
                    if (highlight) cell = cell.Background("#fff6f5");
                    var text = (numeric[i] ? cell.AlignRight() : cell).Text(cells[i]).FontSize(9);
                    if (highlight) text.FontColor(Overdue);
                }
            }
        });
    }

    private static void Section(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(16).PaddingBottom(4).Text(title.ToUpperInvariant())
            .FontSize(9).Bold().FontColor(Muted).LetterSpacing(0.06f);

    private static void Note(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(20).Text(text).FontSize(8).FontColor(Muted);

    private static string Money(decimal value) => "Rs. " + Number(value);

    private static string Number(decimal value) =>
        value.ToString("#,##,##0.00", CultureInfo.GetCultureInfo("en-IN"));
}
