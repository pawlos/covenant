using System.Collections.Generic;
using System.Linq;
using System.Text;
using CallgraphClosure.ILCheck.Core;

namespace CallgraphClosure.ILCheck.Cli;

public static class DiagnosticFormatter
{
    public static string Format(
        string inputPath,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CallgraphClosure IL Check ===");
        sb.AppendLine($"Input: {inputPath}");

        var byCaller = diagnostics
            .GroupBy(d => d.AnnotatedCaller.FullName)
            .ToList();

        sb.AppendLine($"Annotated methods with diagnostics: {byCaller.Count}");
        sb.AppendLine();

        foreach (var group in byCaller)
        {
            sb.AppendLine($"Method {group.Key}:");
            foreach (var d in group)
            {
                var kind = d.Id switch
                {
                    DiagnosticIds.SinkHit when d.Chain.Length > 1
                        => $"[CGC003] {d.SinkLabel} allocation (upgraded from CGC002)",
                    DiagnosticIds.SinkHit
                        => $"[CGC003] {d.SinkLabel} allocation",
                    DiagnosticIds.SourceBoundary
                        => "[CGC001] unannotated source call (unresolved)",
                    DiagnosticIds.ExternalBoundary
                        => "[CGC002] unannotated external call (unresolved)",
                    _ => $"[{d.Id}]",
                };
                sb.AppendLine($"  {kind}");
                foreach (var frame in d.Chain)
                    sb.AppendLine($"    -> {frame.FullName}");
                if (d.UnresolvedTarget is not null)
                    sb.AppendLine($"    (unresolved target: {d.UnresolvedTarget.FullName})");
            }
            sb.AppendLine();
        }

        var counts = diagnostics.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine(
            $"Summary: CGC001={counts.GetValueOrDefault(DiagnosticIds.SourceBoundary, 0)}, " +
            $"CGC002={counts.GetValueOrDefault(DiagnosticIds.ExternalBoundary, 0)}, " +
            $"CGC003={counts.GetValueOrDefault(DiagnosticIds.SinkHit, 0)}");

        return sb.ToString();
    }
}
