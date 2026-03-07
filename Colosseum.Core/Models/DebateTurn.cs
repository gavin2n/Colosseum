namespace Colosseum.Core.Models;

public record DebateTurn(
    Guid Id,
    Guid GladiatorId,
    string GladiatorName,
    string GladiatorAccentColour,
    int Round,
    int TurnIndex,
    string Text,
    List<string> Mentions,
    List<Guid> ReferencedIssueIds,
    DateTimeOffset Timestamp
);
