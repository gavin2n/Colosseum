namespace Colosseum.Core.Models;

public enum MergeDecision { Merge, Distinguish, DismissOne }

public record MergeResolution(
    Guid IssueAId,
    Guid IssueBId,
    MergeDecision Decision,
    string Reason,
    string? GladiatorAName = null,
    string? GladiatorBName = null
);

public record RankedIssue(Guid IssueId, int Rank, string Rationale);

public record DismissedIssue(Guid IssueId, string Reason);

public class Verdict
{
    public List<MergeResolution> MergedIssues { get; init; } = [];
    public List<RankedIssue> RankedIssues { get; init; } = [];
    public List<DismissedIssue> DismissedIssues { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
    public string? RawSummary { get; init; }
    public bool ParseFailed { get; init; }
}
