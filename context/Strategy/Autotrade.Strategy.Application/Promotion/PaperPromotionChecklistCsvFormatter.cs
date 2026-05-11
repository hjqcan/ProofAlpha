using System.Globalization;
using System.Text;

namespace Autotrade.Strategy.Application.Promotion;

public static class PaperPromotionChecklistCsvFormatter
{
    public static string Format(PaperPromotionChecklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);

        var sb = new StringBuilder();
        sb.AppendLine("table,checklist");
        sb.AppendLine("generated_at_utc,session_id,overall_status,can_consider_live,live_arming_unchanged,residual_risks");
        sb.AppendLine(string.Join(",",
            checklist.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            checklist.SessionId,
            Csv(checklist.OverallStatus),
            checklist.CanConsiderLive.ToString(CultureInfo.InvariantCulture),
            checklist.LiveArmingUnchanged.ToString(CultureInfo.InvariantCulture),
            Csv(string.Join(" | ", checklist.ResidualRisks))));
        sb.AppendLine();

        sb.AppendLine("table,criteria");
        sb.AppendLine("criterion_id,name,status,reason,evidence_ids,residual_risks");
        foreach (var criterion in checklist.Criteria)
        {
            sb.AppendLine(string.Join(",",
                Csv(criterion.Id),
                Csv(criterion.Name),
                Csv(criterion.Status),
                Csv(criterion.Reason),
                Csv(string.Join("|", criterion.EvidenceIds)),
                Csv(string.Join(" | ", criterion.ResidualRisks))));
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
