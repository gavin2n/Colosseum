namespace Colosseum.Core.Models;

public enum SessionStatus { Pending, Running, Complete, Cancelled, Failed }

public class ReviewSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PrUrl { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public string? PrTitle { get; set; }
    public required string Diff { get; init; }
    public required List<Gladiator> Squad { get; init; }
    public int MaxRounds { get; init; } = 2;
    public SessionStatus Status { get; set; } = SessionStatus.Pending;
    public List<DebateTurn> Turns { get; } = [];
    public List<Issue> Issues { get; } = [];
    public Verdict? Verdict { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
