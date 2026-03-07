namespace Colosseum.Core.Models;

public enum IssueSeverity { High, Medium, Low }

public enum IssueStatus { Open, MergeCandidate, Dismissed }

public class Issue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IssueSeverity Severity { get; init; }
    public required Guid RaisedByGladiatorId { get; init; }
    public required string RaisedByGladiatorName { get; init; }
    public IssueStatus Status { get; set; } = IssueStatus.Open;
    public Guid? MergeCandidate { get; set; }
    public List<IssueDebateEntry> DebateTrail { get; } = [];
    public int VotesFor { get; set; } = 1;
    public int VotesAgainst { get; set; }
    public DateTimeOffset RaisedAt { get; init; } = DateTimeOffset.UtcNow;
}
