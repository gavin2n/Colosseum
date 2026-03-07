namespace Colosseum.Core.Models;

public enum Stance { Raised, Supports, Disputes, Seconds }

public record IssueDebateEntry(
    Guid GladiatorId,
    string GladiatorName,
    Stance Stance,
    string Text,
    DateTimeOffset Timestamp
);
