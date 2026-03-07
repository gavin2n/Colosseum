namespace Colosseum.Core.Models;

public record Gladiator(
    Guid Id,
    string Name,
    string Domain,
    string Personality,
    string MentionStyle,
    string AccentColour,
    bool IsArbiter = false
)
{
    public static readonly Gladiator Maximus = new(
        Guid.Parse("11111111-0000-0000-0000-000000000001"),
        "Maximus", "Performance",
        "Aggressive, cites Big-O, hates allocations. Never satisfied with 'good enough'.",
        "@Cassius your service boundary is elegant but useless if it's O(n²)",
        "#c94a0e");

    public static readonly Gladiator Brutus = new(
        Guid.Parse("11111111-0000-0000-0000-000000000002"),
        "Brutus", "DRY / YAGNI / Clean Code",
        "Pedantic, quotes Fowler, hunts duplication. Considers copy-paste a personal insult.",
        "@Cassius that's three layers for a two-line fix",
        "#6a8fc7");

    public static readonly Gladiator Cassius = new(
        Guid.Parse("11111111-0000-0000-0000-000000000003"),
        "Cassius", "DDD / Clean Abstractions / SRP",
        "Philosophical, guards domain boundaries. Patient but firm.",
        "@Brutus the duplication is a symptom — the missing boundary is the disease",
        "#7a9e6a");

    public static readonly Gladiator Valeria = new(
        Guid.Parse("11111111-0000-0000-0000-000000000004"),
        "Valeria", "Security",
        "Paranoid, assumes breach. Flags every unvalidated input. Treats trust as a vulnerability.",
        "@Maximus performance is irrelevant if the service is already compromised",
        "#9a6ab0");

    public static readonly Gladiator Arbiter = new(
        Guid.Parse("11111111-0000-0000-0000-000000000005"),
        "Arbiter", "Moderator",
        "Calm, impartial. Renders final verdict. Never participates in debate.",
        string.Empty,
        "#c9a84c",
        IsArbiter: true);

    public static IReadOnlyList<Gladiator> DefaultSquad =>
        [Maximus, Brutus, Cassius, Valeria];
}
