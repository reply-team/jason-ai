using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Reports;

/// <summary>
/// One shape for a report on the wire, so a read and an admission answer with the same thing. The correlation
/// always says <c>verified: false</c>: what the runtime checked is that the ids it was given exist, and saying
/// that out loud is cheaper than letting a later reader assume more.
/// </summary>
public static class ReportMapper
{
    public static ReportDto ToDto(Report report, ReportDedupDto dedup)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReportDto(
            report.PublicId,
            Utc(report.ReceivedAt),
            new ActorRef(report.ReporterType, report.ReporterId),
            report.Reason,
            dedup,
            report.Assertion,
            report.AssertionHash,
            Correlation(report));
    }

    public static ReportSummaryDto ToSummary(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReportSummaryDto(
            report.PublicId,
            Utc(report.ReceivedAt),
            new ActorRef(report.ReporterType, report.ReporterId),
            report.Effect,
            report.Tool,
            report.Provider,
            report.Summary,
            Correlation(report));
    }

    private static ReportCorrelationDto Correlation(Report report) =>
        new(
            report.Campaign?.PublicId,
            report.Contact?.PublicId,
            report.WorkItem?.PublicId,
            report.Operation,
            report.OperationKnown,
            report.ContactInCampaign,
            Verified: false);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
