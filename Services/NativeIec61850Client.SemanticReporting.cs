using System;
using System.Linq;
using AR.Iec61850.Discovery;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// P5.4 report-value projection bridge. Report protocol/member identity stays engine-owned;
/// this layer only supplies the already-authoritative per-IED model so structured static
/// DataSet members can fan out to proven scalar descendants before ARSAS updates live points.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private LiveIedModelDiscoveryDocument? _semanticReportProjectionAuthorityModel;
    private LiveIedModelDiscoveryDocument? _semanticReportProjectionContextModel;
    private ArMms.MmsReportSemanticProjectionContext? _semanticReportProjectionContext;

    private void SetSemanticReportProjectionAuthority(LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(_semanticReportProjectionAuthorityModel, model))
            return;

        _semanticReportProjectionAuthorityModel = model;
        _semanticReportProjectionContextModel = null;
        _semanticReportProjectionContext = null;
    }

    private void ResetSemanticReportProjectionContext()
    {
        _semanticReportProjectionAuthorityModel = null;
        _semanticReportProjectionContextModel = null;
        _semanticReportProjectionContext = null;
    }

    private ArMms.MmsReportValueProjection ProjectReportValue(ArMms.MmsReportFrame report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Hybrid FAT uses the exact planning authority (live model or opened SCL design).
        // Generic report callers use the fresh live model built by reporting discovery.
        // No global/ambient semantic state is used, so model evidence cannot cross IEDs.
        var model = _semanticReportProjectionAuthorityModel ?? _liveModel;
        if (model is null)
            return ArMms.MmsReportValueProjector.Project(report);

        try
        {
            if (_semanticReportProjectionContext is null ||
                !ReferenceEquals(_semanticReportProjectionContextModel, model))
            {
                _semanticReportProjectionContext = ArMms.MmsReportSemanticProjectionContext.Create(model);
                _semanticReportProjectionContextModel = model;
            }

            return ArMms.MmsSemanticReportValueProjector.Project(
                report,
                _semanticReportProjectionContext);
        }
        catch (Exception ex)
        {
            // Projection must never invent evidence or tear down a healthy MMS association.
            // Preserve the established raw update and expose an explicit fail-closed warning.
            var baseline = ArMms.MmsReportValueProjector.Project(report);
            return new ArMms.MmsReportValueProjection
            {
                Updates = baseline.Updates,
                Warnings = baseline.Warnings
                    .Concat(new[]
                    {
                        $"REPORT_SEMANTIC_FALLBACK: model-backed projection failed closed: {ex.GetType().Name}: {ex.Message}"
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }
}
