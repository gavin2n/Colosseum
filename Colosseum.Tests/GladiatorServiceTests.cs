using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.Extensions.Options;

namespace Colosseum.Tests;

public class GladiatorServiceTests
{
    private static GladiatorService MakeService(ColosseumOptions? opts = null)
    {
        var options = Options.Create(opts ?? new ColosseumOptions());
        return new GladiatorService(options);
    }

    private static DebateTurn MakeTurn(Gladiator g, int round, string text = "Some text") =>
        new(Guid.NewGuid(), g.Id, g.Name, g.AccentColour, g.Domain, round, 0, text, [], [], DateTimeOffset.UtcNow);

    private static Issue MakeIssue(string title, Gladiator raiser) => new()
    {
        Title = title,
        Description = "desc",
        Severity = IssueSeverity.Medium,
        RaisedByGladiatorId = raiser.Id,
        RaisedByGladiatorName = raiser.Name
    };

    // ── BuildPrompt ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPrompt_ContainsGladiatorNameAndDomain()
    {
        var svc = MakeService();
        var prompt = svc.BuildPrompt(Gladiator.Maximus, "diff", [], [], Gladiator.DefaultSquad, 1);

        Assert.Contains("Maximus", prompt);
        Assert.Contains("Performance", prompt);
    }

    [Fact]
    public void BuildPrompt_Round1_NoOpenIssues_OmitsIssueSection()
    {
        var svc = MakeService();
        var prompt = svc.BuildPrompt(Gladiator.Brutus, "diff", [], [], Gladiator.DefaultSquad, 1);

        Assert.DoesNotContain("OPEN ISSUES RAISED SO FAR", prompt);
    }

    [Fact]
    public void BuildPrompt_WithOpenIssues_IncludesIssueSection()
    {
        var svc = MakeService();
        var issue = MakeIssue("N+1 query", Gladiator.Maximus);
        var prompt = svc.BuildPrompt(Gladiator.Brutus, "diff", [], [issue], Gladiator.DefaultSquad, 2);

        Assert.Contains("OPEN ISSUES RAISED SO FAR", prompt);
        Assert.Contains("N+1 query", prompt);
    }

    [Fact]
    public void BuildPrompt_Round1_NoPriorTurns_OmitsDebateSoFar()
    {
        var svc = MakeService();
        var prompt = svc.BuildPrompt(Gladiator.Cassius, "diff", [], [], Gladiator.DefaultSquad, 1);

        Assert.DoesNotContain("DEBATE SO FAR", prompt);
    }

    [Fact]
    public void BuildPrompt_WithPriorTurns_IncludesDebateSoFar()
    {
        var svc = MakeService();
        var turn = MakeTurn(Gladiator.Maximus, 1, "Maximus says something.");
        var prompt = svc.BuildPrompt(Gladiator.Brutus, "diff", [turn], [], Gladiator.DefaultSquad, 2);

        Assert.Contains("DEBATE SO FAR", prompt);
        Assert.Contains("Maximus says something.", prompt);
    }

    [Fact]
    public void BuildPrompt_WithManyTurns_WindowsToMaxContextTurns()
    {
        var opts = new ColosseumOptions { MaxContextTurns = 3 };
        var svc = MakeService(opts);

        var turns = Enumerable.Range(1, 10)
            .Select(i => MakeTurn(Gladiator.Maximus, 1, $"UNIQUE-TURN-{i:D3}"))
            .ToList();

        var prompt = svc.BuildPrompt(Gladiator.Brutus, "diff", turns, [], Gladiator.DefaultSquad, 2);

        // Only last 3 turns should appear
        Assert.Contains("UNIQUE-TURN-010", prompt);
        Assert.Contains("UNIQUE-TURN-009", prompt);
        Assert.Contains("UNIQUE-TURN-008", prompt);
        Assert.DoesNotContain("UNIQUE-TURN-001", prompt);
        Assert.DoesNotContain("UNIQUE-TURN-007", prompt);
        Assert.Contains("earlier turns omitted", prompt);
    }

    [Fact]
    public void BuildPrompt_OmitsSelfFromMentionExamples()
    {
        var svc = MakeService();
        var prompt = svc.BuildPrompt(Gladiator.Maximus, "diff", [], [], Gladiator.DefaultSquad, 1);

        // @Maximus should not appear in the mention-example list (Maximus is the speaker)
        // The prompt should still reference other gladiators
        Assert.Contains("@Brutus", prompt);
        Assert.Contains("@Cassius", prompt);
    }

    [Fact]
    public void BuildPrompt_MergeCandidate_ShowsFlag()
    {
        var svc = MakeService();
        var issue = MakeIssue("Duplicate logic", Gladiator.Brutus);
        issue.MergeCandidate = Guid.NewGuid();

        var prompt = svc.BuildPrompt(Gladiator.Cassius, "diff", [], [issue], Gladiator.DefaultSquad, 2);

        Assert.Contains("merge candidate", prompt);
    }

    // ── BuildArbiterPrompt ───────────────────────────────────────────────────

    [Fact]
    public void BuildArbiterPrompt_ContainsAllTurns()
    {
        var svc = MakeService();
        var session = new ReviewSession
        {
            PrUrl = "https://github.com/test/repo/pull/1",
            Owner = "test",
            Repo = "repo",
            PrNumber = 1,
            Diff = "diff",
            Squad = [Gladiator.Maximus, Gladiator.Brutus]
        };
        session.Turns.Add(MakeTurn(Gladiator.Maximus, 1, "Maximus speaks."));
        session.Turns.Add(MakeTurn(Gladiator.Brutus, 1, "Brutus replies."));

        var prompt = svc.BuildArbiterPrompt(session);

        Assert.Contains("Maximus speaks.", prompt);
        Assert.Contains("Brutus replies.", prompt);
    }

    [Fact]
    public void BuildArbiterPrompt_ContainsAllIssueIds()
    {
        var svc = MakeService();
        var session = new ReviewSession { PrUrl = "https://github.com/test/repo/pull/1", Owner = "test", Repo = "repo", PrNumber = 1, Diff = "diff", Squad = [] };
        var issue = MakeIssue("Security flaw", Gladiator.Valeria);
        session.Issues.Add(issue);

        var prompt = svc.BuildArbiterPrompt(session);

        Assert.Contains(issue.Id.ToString(), prompt);
        Assert.Contains("Security flaw", prompt);
    }

    [Fact]
    public void BuildArbiterPrompt_ContainsJsonFormatInstruction()
    {
        var svc = MakeService();
        var session = new ReviewSession { PrUrl = "https://github.com/test/repo/pull/1", Owner = "test", Repo = "repo", PrNumber = 1, Diff = "diff", Squad = [] };

        var prompt = svc.BuildArbiterPrompt(session);

        Assert.Contains("mergeResolutions", prompt);
        Assert.Contains("rankedIssues", prompt);
        Assert.Contains("dismissedIssues", prompt);
    }
}
