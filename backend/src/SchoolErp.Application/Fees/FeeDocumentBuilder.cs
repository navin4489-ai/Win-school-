using System.Globalization;
using System.Text;
using SchoolErp.Application.Fees;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Fees;

/// <summary>
/// Renders printable fee documents as self-contained HTML.
///
/// HTML keeps the deployment free of a PDF renderer while still giving parents a
/// receipt they can save or print from the browser (the page carries a print
/// stylesheet, so "Save as PDF" produces a clean document).
/// </summary>
public static class FeeDocumentBuilder
{
    private const string Accent = "#0f2c4d";
    private const string Muted = "#5b7186";

    public static FeeDocumentDto BuildReceipt(School school, string schoolCode, Student student,
        string className, Payment payment, string installmentName, decimal penaltyTotal)
    {
        var currency = Format(payment.Amount);
        var rows = new StringBuilder();

        rows.Append(Row("Receipt number", payment.ReceiptNo));
        rows.Append(Row("Payment date", payment.PaidAtUtc.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)));
        rows.Append(Row("Amount received", currency, true));
        rows.Append(Row("Payment mode", payment.Mode.ToString()));
        rows.Append(Row("Reference", payment.ReferenceNo ?? payment.GatewayPaymentId ?? "-"));

        if (payment.RefundedAmount > 0)
            rows.Append(Row("Refunded", "-" + Format(payment.RefundedAmount)));

        var breakdown = new StringBuilder();
        breakdown.Append(Row("Fee", Format(payment.Amount - Math.Min(payment.Amount, penaltyTotal))));
        breakdown.Append(Row("Late fee", Format(Math.Min(payment.Amount, penaltyTotal))));
        breakdown.Append(Row("Total paid", currency, true));

        var html = Layout(
            school,
            "Fee Receipt",
            $"Receipt {payment.ReceiptNo}",
            $@"<table class='kv'>{rows}</table>
               <h3>Breakdown</h3>
               <table class='kv'>{breakdown}</table>
               <p class='note'>Installment: {Escape(installmentName)}. This is a computer generated receipt.</p>",
            "Receipt");

        return new FeeDocumentDto
        {
            FileName = $"Receipt-{payment.ReceiptNo}.html",
            Html = html
        };
    }

    public static FeeDocumentDto BuildStatement(School school, Student student, string className,
        StudentLedgerDto ledger)
    {
        var installments = new StringBuilder();
        foreach (var installment in ledger.Installments)
        {
            var style = installment.IsOverdue ? " class='overdue'" : string.Empty;
            installments.Append($@"<tr{style}>
                <td>{Escape(installment.Name)}</td>
                <td>{installment.DueDate:dd MMM yyyy}</td>
                <td class='num'>{Format(installment.Amount)}</td>
                <td class='num'>{Format(installment.PenaltyAmount)}</td>
                <td class='num'>{Format(installment.PaidAmount)}</td>
                <td class='num'>{Format(installment.Balance)}</td>
                <td>{Escape(installment.Status.ToString())}{(installment.IsOverdue ? " (overdue)" : string.Empty)}</td>
            </tr>");
        }

        var payments = new StringBuilder();
        foreach (var payment in ledger.Payments)
        {
            payments.Append($@"<tr>
                <td>{payment.PaidAtUtc:dd MMM yyyy}</td>
                <td>{Escape(payment.ReceiptNo)}</td>
                <td>{Escape(payment.Mode.ToString())}</td>
                <td class='num'>{Format(payment.Amount)}</td>
                <td>{Escape(payment.Status.ToString())}</td>
            </tr>");
        }

        var html = Layout(
            school,
            "Fee Statement",
            student.FullName,
            $@"<table class='kv'>
                 {Row("Admission number", student.AdmissionNo)}
                 {Row("Class", className)}
                 {Row("Total billed", Format(ledger.TotalBilled))}
                 {Row("Discount", "-" + Format(ledger.Discount))}
                 {(ledger.PlatformFee > 0 ? Row("Platform fee (included)", Format(ledger.PlatformFee)) : string.Empty)}
                 {Row("Total paid", Format(ledger.TotalPaid))}
                 {Row("Late fee applied", Format(ledger.PenaltyTotal))}
                 {Row("Outstanding balance", Format(ledger.Balance), true)}
               </table>
               <h3>Installments</h3>
               <table class='grid'>
                 <thead><tr><th>Installment</th><th>Due date</th><th class='num'>Fee</th><th class='num'>Late fee</th>
                 <th class='num'>Paid</th><th class='num'>Balance</th><th>Status</th></tr></thead>
                 <tbody>{installments}</tbody>
               </table>
               <h3>Payments</h3>
               <table class='grid'>
                 <thead><tr><th>Date</th><th>Receipt</th><th>Mode</th><th class='num'>Amount</th><th>Status</th></tr></thead>
                 <tbody>{payments}</tbody>
               </table>
               <p class='note'>Generated on {DateTime.UtcNow:dd MMM yyyy, HH:mm} UTC. This is a computer generated statement.</p>",
            "Statement");

        return new FeeDocumentDto
        {
            FileName = $"Fee-Statement-{student.AdmissionNo}.html",
            Html = html
        };
    }

    private static string Layout(School school, string heading, string subHeading, string body, string tag)
    {
        var schoolName = string.IsNullOrWhiteSpace(school.Name) ? "School" : school.Name;
        var address = string.Join(", ", new[]
        {
            school.AddressLine1, school.AddressLine2, school.City, school.State, school.PostalCode
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return $@"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>{Escape(heading)} - {Escape(schoolName)}</title>
<style>
  :root {{ color-scheme: light; }}
  * {{ box-sizing: border-box; }}
  body {{ font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; margin: 0; padding: 32px; color: #16232f; background: #f6f8fb; }}
  .sheet {{ max-width: 820px; margin: 0 auto; background: #fff; border-radius: 14px; padding: 36px;
            box-shadow: 0 12px 32px rgba(15, 44, 77, .10); }}
  header {{ display: flex; justify-content: space-between; align-items: flex-start; gap: 24px;
            border-bottom: 3px solid {Accent}; padding-bottom: 18px; margin-bottom: 24px; }}
  h1 {{ margin: 0 0 4px; font-size: 21px; color: {Accent}; }}
  .school {{ font-weight: 700; font-size: 16px; color: {Accent}; }}
  .muted {{ color: {Muted}; font-size: 12px; }}
  .tag {{ background: #eef3f8; color: {Accent}; border-radius: 999px; padding: 6px 14px;
          font-size: 11px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; white-space: nowrap; }}
  h3 {{ margin: 26px 0 10px; font-size: 14px; text-transform: uppercase; letter-spacing: .07em; color: {Muted}; }}
  table {{ width: 100%; border-collapse: collapse; }}
  table.kv td {{ padding: 7px 0; border-bottom: 1px solid #edf1f5; font-size: 14px; }}
  table.kv td:first-child {{ color: {Muted}; width: 45%; }}
  table.kv td.strong {{ font-weight: 700; color: {Accent}; }}
  table.grid {{ font-size: 13px; border: 1px solid #e3e9ef; border-radius: 10px; overflow: hidden; }}
  table.grid th {{ background: #f2f6fa; text-align: left; padding: 10px 12px; font-size: 11px;
                   text-transform: uppercase; letter-spacing: .05em; color: {Muted}; }}
  table.grid td {{ padding: 9px 12px; border-top: 1px solid #edf1f5; }}
  .num {{ text-align: right; font-variant-numeric: tabular-nums; }}
  tr.overdue td {{ background: #fff6f5; color: #a8342a; }}
  .note {{ margin-top: 26px; font-size: 11px; color: {Muted}; }}
  .actions {{ max-width: 820px; margin: 16px auto 0; display: flex; gap: 10px; }}
  button {{ font: inherit; font-size: 13px; font-weight: 600; padding: 9px 18px; border-radius: 9px;
            border: 1px solid {Accent}; background: {Accent}; color: #fff; cursor: pointer; }}
  button.secondary {{ background: #fff; color: {Accent}; }}
  @media print {{ body {{ background: #fff; padding: 0; }} .sheet {{ box-shadow: none; padding: 0; }}
                  .actions {{ display: none; }} }}
</style>
</head>
<body>
  <div class='sheet'>
    <header>
      <div>
        <div class='school'>{Escape(schoolName)}</div>
        <div class='muted'>{Escape(address)}</div>
        <h1>{Escape(heading)}</h1>
        <div class='muted'>{Escape(subHeading)}</div>
      </div>
      <div class='tag'>{Escape(tag)}</div>
    </header>
    {body}
  </div>
  <div class='actions'>
    <button onclick='window.print()'>Print or Save as PDF</button>
    <button class='secondary' onclick='history.back()'>Back</button>
  </div>
</body>
</html>";
    }

    private static string Row(string label, string value, bool strong = false) =>
        $"<tr><td>{Escape(label)}</td><td{(strong ? " class='strong'" : string.Empty)}>{Escape(value)}</td></tr>";

    private static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Escape(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
