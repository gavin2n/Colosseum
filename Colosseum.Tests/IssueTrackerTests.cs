using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Colosseum.Tests;

public class IssueTrackerTests
{
    private IssueTracker MakeTracker() => new(NullLogger<IssueTracker>.Instance);

    [Fact]
    public void ParseAndApply_IssueTag_CreatesIssue()
    {
        var tracker = MakeTracker();
        var text = "Line 47 is a mess. [ISSUE: N+1 query in loop | severity: high]";
        var (newIssues, _) = tracker.ParseAndApply(text, Gladiator.Maximus, []);
        Assert.Single(newIssues);
        Assert.Equal("N+1 query in loop", newIssues[0].Title);
        Assert.Equal(IssueSeverity.High, newIssues[0].Severity);
    }

    [Fact]
    public void ParseAndApply_MultipleIssueTags_CreatesAll()
    {
        var tracker = MakeTracker();
        var text = "[ISSUE: Issue A | severity: high] and [ISSUE: Issue B | severity: low]";
        var (newIssues, _) = tracker.ParseAndApply(text, Gladiator.Brutus, []);
        Assert.Equal(2, newIssues.Count);
    }

    [Fact]
    public void ParseAndApply_OverlapTag_ReturnsMatchedTitle()
    {
        var tracker = MakeTracker();
        var existing = new Issue
        {
            Title = "N+1 query in loop",
            Description = "desc",
            Severity = IssueSeverity.High,
            RaisedByGladiatorId = Gladiator.Maximus.Id,
            RaisedByGladiatorName = "Maximus"
        };

        var text = "[OVERLAP: N+1 query in loop] — same root cause.";
        var (_, overlapTitles) = tracker.ParseAndApply(text, Gladiator.Cassius, [existing]);
        Assert.Contains("N+1 query in loop", overlapTitles);
    }

    [Fact]
    public void FlagMergeCandidate_SetsBothIssues()
    {
        var tracker = MakeTracker();
        var a = new Issue { Title = "A", Description = "", Severity = IssueSeverity.High, RaisedByGladiatorId = Gladiator.Maximus.Id, RaisedByGladiatorName = "Maximus" };
        var b = new Issue { Title = "B", Description = "", Severity = IssueSeverity.Medium, RaisedByGladiatorId = Gladiator.Brutus.Id, RaisedByGladiatorName = "Brutus" };

        tracker.FlagMergeCandidate(a, b);

        Assert.Equal(b.Id, a.MergeCandidate);
        Assert.Equal(a.Id, b.MergeCandidate);
        Assert.Equal(IssueStatus.MergeCandidate, a.Status);
        Assert.Equal(IssueStatus.MergeCandidate, b.Status);
    }

    [Fact]
    public void ParseAndApply_OverlapTag_UnknownTitle_Ignored()
    {
        var tracker = MakeTracker();
        var text = "[OVERLAP: completely unknown issue title xyz]";
        var (_, overlapTitles) = tracker.ParseAndApply(text, Gladiator.Valeria, []);
        Assert.Empty(overlapTitles);
    }

    [Fact]
    public void ParseAndApply_OverlapTag_AddsOverlapStanceNotDisputes()
    {
        var tracker = MakeTracker();
        var existing = new Issue
        {
            Title = "N+1 query in loop",
            Description = "desc",
            Severity = IssueSeverity.High,
            RaisedByGladiatorId = Gladiator.Maximus.Id,
            RaisedByGladiatorName = "Maximus"
        };

        var text = "[OVERLAP: N+1 query in loop] — I see the same root cause.";
        tracker.ParseAndApply(text, Gladiator.Cassius, [existing]);

        var overlapEntry = existing.DebateTrail.FirstOrDefault(e => e.GladiatorId == Gladiator.Cassius.Id);
        Assert.NotNull(overlapEntry);
        Assert.Equal(Stance.Overlap, overlapEntry.Stance);
        Assert.NotEqual(Stance.Disputes, overlapEntry.Stance);
    }

    // ── Stance signal parsing ─────────────────────────────────────────────────

    [Fact]
    public void ParseAndApply_SupportSignal_IncrementsVotesFor()
    {
        var tracker = MakeTracker();
        var raiserIssue = new Issue
        {
            Title = "Security flaw",
            Description = "desc",
            Severity = IssueSeverity.High,
            RaisedByGladiatorId = Gladiator.Valeria.Id,
            RaisedByGladiatorName = "Valeria"
        };

        // Support signal: "@Valeria is correct here"
        var text = "@Valeria is correct here, the input is unvalidated.";
        tracker.ParseAndApply(text, Gladiator.Cassius, [raiserIssue]);

        Assert.Equal(2, raiserIssue.VotesFor); // 1 initial + 1 support
    }

    [Fact]
    public void ParseAndApply_SupportSignal_AddsSecondsEntry()
    {
        var tracker = MakeTracker();
        var raiserIssue = new Issue
        {
            Title = "Missing null check",
            Description = "desc",
            Severity = IssueSeverity.Medium,
            RaisedByGladiatorId = Gladiator.Brutus.Id,
            RaisedByGladiatorName = "Brutus"
        };

        tracker.ParseAndApply("@Brutus is right about this.", Gladiator.Maximus, [raiserIssue]);

        Assert.Contains(raiserIssue.DebateTrail, e => e.Stance == Stance.Seconds);
    }

    [Fact]
    public void ParseAndApply_DisputeSignal_IncrementsVotesAgainst()
    {
        var tracker = MakeTracker();
        var raiserIssue = new Issue
        {
            Title = "Over-engineering concern",
            Description = "desc",
            Severity = IssueSeverity.Low,
            RaisedByGladiatorId = Gladiator.Cassius.Id,
            RaisedByGladiatorName = "Cassius"
        };

        tracker.ParseAndApply("I dispute @Cassius on this — it's not over-engineering.", Gladiator.Maximus, [raiserIssue]);

        Assert.Equal(1, raiserIssue.VotesAgainst);
    }

    [Fact]
    public void ParseAndApply_DisputeSignal_AddsDisputesEntry()
    {
        var tracker = MakeTracker();
        var raiserIssue = new Issue
        {
            Title = "Premature abstraction",
            Description = "desc",
            Severity = IssueSeverity.Low,
            RaisedByGladiatorId = Gladiator.Brutus.Id,
            RaisedByGladiatorName = "Brutus"
        };

        tracker.ParseAndApply("I disagree with @Brutus here.", Gladiator.Valeria, [raiserIssue]);

        Assert.Contains(raiserIssue.DebateTrail, e => e.Stance == Stance.Disputes);
    }

    [Fact]
    public void ParseAndApply_SupportForUnknownGladiator_IsNoOp()
    {
        var tracker = MakeTracker();
        var someIssue = new Issue
        {
            Title = "Some issue",
            Description = "desc",
            Severity = IssueSeverity.Low,
            RaisedByGladiatorId = Gladiator.Maximus.Id,
            RaisedByGladiatorName = "Maximus"
        };
        var initialVotes = someIssue.VotesFor;

        // "Caesar" doesn't exist — no issue should be credited
        tracker.ParseAndApply("@Caesar is correct about everything.", Gladiator.Brutus, [someIssue]);

        Assert.Equal(initialVotes, someIssue.VotesFor);
    }

    // ── No internal dead state ────────────────────────────────────────────────

    [Fact]
    public void ParseAndApply_TwoCalls_DoesNotAccumulateInternalState()
    {
        // IssueTracker has no internal _issues list — each call is pure.
        // Verify two separate parse calls yield independent results.
        var tracker = MakeTracker();

        var (issues1, _) = tracker.ParseAndApply(
            "[ISSUE: Issue A | severity: high]", Gladiator.Maximus, []);
        var (issues2, _) = tracker.ParseAndApply(
            "[ISSUE: Issue B | severity: low]", Gladiator.Brutus, []);

        Assert.Single(issues1);
        Assert.Equal("Issue A", issues1[0].Title);
        Assert.Single(issues2);
        Assert.Equal("Issue B", issues2[0].Title);
    }
}
