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
}
