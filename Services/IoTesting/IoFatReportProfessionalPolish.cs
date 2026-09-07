using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Final customer-facing polish for the SCL FAT report command stream. This deliberately
/// operates after the evidence layout is built so presentation improvements cannot alter
/// source identity, test scope, evidence state, timestamps, or assessment results.
/// </summary>
internal static class IoFatReportProfessionalPolish
{
    // IoFatV2ReportLayoutEngine columns: 30 margin + 24 number column + 5 cell inset.
    private const double SignalCellTextX = 59d;
    private const double TypeCellTextX = 454d;

    public static IoFatReportLayoutPlan Apply(IoTestProject project, IoFatReportLayoutPlan layout)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(layout);

        var points = project.Ieds
            .SelectMany(ied => ied.TestPoints.Where(point => point.IsIncludedInFat))
            .ToArray();
        var pointIndex = 0;
        var polishedPages = new List<IoFatReportPagePlan>(layout.Pages.Count);

        foreach (var page in layout.Pages)
        {
            var commands = new List<IoFatReportCommand>(page.Commands.Count);
            foreach (var command in page.Commands)
            {
                if (command is not IoFatReportTextCommand text)
                {
                    commands.Add(command);
                    continue;
                }

                var updated = text;

                if (Nearly(text.X, SignalCellTextX) &&
                    !text.Text.Equals("Signal", StringComparison.OrdinalIgnoreCase) &&
                    pointIndex < points.Length)
                {
                    updated = updated with
                    {
                        Text = IoFatSignalDisplayNameFormatter.Format(points[pointIndex]),
                        FontSize = Math.Max(updated.FontSize, 6.1d)
                    };
                    pointIndex++;
                }
                else if (Nearly(text.X, TypeCellTextX) && text.Text.Equals("Other", StringComparison.OrdinalIgnoreCase))
                {
                    updated = updated with { Text = "Composite" };
                }

                // One visual type family throughout the report. "Mono" remains useful as a
                // layout semantic elsewhere, but report output must not visually switch to a
                // terminal/typewriter face for IEC references or timestamps.
                if (updated.Font == IoFatReportFontKind.Mono)
                    updated = updated with { Font = IoFatReportFontKind.Regular };

                // The first relay-bench PDF proved that 4.9–5.1 pt reference/timestamp text
                // is technically legible on screen but too weak when printed. Raise only the
                // micro-text floor; larger hierarchy levels are left untouched.
                if (updated.FontSize < 5.4d)
                    updated = updated with { FontSize = 5.4d };
                if (Nearly(updated.BaselineY, 24d) && updated.FontSize < 6.3d)
                    updated = updated with { FontSize = 6.3d };

                commands.Add(updated);
            }

            polishedPages.Add(page with { Commands = commands.ToArray() });
        }

        return layout with { Pages = polishedPages.ToArray() };
    }

    private static bool Nearly(double left, double right)
        => Math.Abs(left - right) < 0.05d;
}
