using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Colosseum.Tests;

public class ModeratorServiceTests
{
    private static (ModeratorService Service, Mock<IClaudeProvider> Claude) MakeService()
    {
        var claude = new Mock<IClaudeProvider>();
        var opts = Options.Create(new ColosseumOptions());
        var gladiatorService = new GladiatorService(opts);
        var svc = new ModeratorService(
            claude.Object, gladiatorService, opts,
            NullLogger<ModeratorService>.Instance);
        return (svc, claude);
    }

    private static ReviewSession MakeSession(params Issue[] issues)
    {
        var session = new ReviewSession
        {
            PrUrl = "https://github.com/test/repo/pull/1",
            Owner = "test",
            Repo = "repo",
            PrNumber = 1,
            Diff = "diff",
            Squad = [Gladiator.Maximus]
        };
        foreach (var i in issues) session.Issues.Add(i);
        return session;
    }

    private static Issue MakeIssue(Guid? id = null) => new()
    {
        Title = "Test issue",
        Description = "desc",
        Severity = IssueSeverity.Medium,
        RaisedByGladiatorId = Gladiator.Maximus.Id,
        RaisedByGladiatorName = "Maximus"
    };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderVerdictAsync_ValidJson_PopulatesVerdict()
    {
        var (svc, claude) = MakeService();
        var issue = MakeIssue();
        var session = MakeSession(issue);

        var json = $$"""
            {
              "mergeResolutions": [],
              "rankedIssues": [{ "issueId": "{{issue.Id}}", "rank": 1, "rationale": "Most critical" }],
              "dismissedIssues": [],
              "summary": "Good PR overall."
            }
            """;

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(json);

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.False(verdict.ParseFailed);
        Assert.Single(verdict.RankedIssues);
        Assert.Equal(1, verdict.RankedIssues[0].Rank);
        Assert.Equal("Most critical", verdict.RankedIssues[0].Rationale);
        Assert.Equal("Good PR overall.", verdict.Summary);
    }

    [Fact]
    public async Task RenderVerdictAsync_WithMergeResolution_Parsed()
    {
        var (svc, claude) = MakeService();
        var a = MakeIssue();
        var b = MakeIssue();
        var session = MakeSession(a, b);

        var json = $$"""
            {
              "mergeResolutions": [{ "issueAId": "{{a.Id}}", "issueBId": "{{b.Id}}", "decision": "Merge", "reason": "Same root cause" }],
              "rankedIssues": [],
              "dismissedIssues": [],
              "summary": ""
            }
            """;

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(json);

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.False(verdict.ParseFailed);
        Assert.Single(verdict.MergedIssues);
        Assert.Equal(MergeDecision.Merge, verdict.MergedIssues[0].Decision);
    }

    // ── Malformed responses ──────────────────────────────────────────────────

    [Fact]
    public async Task RenderVerdictAsync_NoJsonInResponse_ParseFailed()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("I cannot provide a verdict at this time.");

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.True(verdict.ParseFailed);
        Assert.Contains("I cannot provide", verdict.RawSummary);
    }

    [Fact]
    public async Task RenderVerdictAsync_InvalidGuidInJson_ParseFailed()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();

        var json = """
            {
              "mergeResolutions": [],
              "rankedIssues": [{ "issueId": "not-a-guid", "rank": 1, "rationale": "broken" }],
              "dismissedIssues": [],
              "summary": ""
            }
            """;

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(json);

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.True(verdict.ParseFailed);
    }

    [Fact]
    public async Task RenderVerdictAsync_MissingOptionalSections_ReturnsEmptyLists()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();

        // Only summary — no arrays at all
        var json = """{ "summary": "Short and simple." }""";

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(json);

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.False(verdict.ParseFailed);
        Assert.Empty(verdict.MergedIssues);
        Assert.Empty(verdict.RankedIssues);
        Assert.Empty(verdict.DismissedIssues);
        Assert.Equal("Short and simple.", verdict.Summary);
    }

    [Fact]
    public async Task RenderVerdictAsync_ClaudeThrows_ParseFailedWithMessage()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("CLI crashed"));

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.True(verdict.ParseFailed);
        Assert.Equal("Arbiter call failed.", verdict.RawSummary);
    }

    [Fact]
    public async Task RenderVerdictAsync_JsonWrappedInProseWithBrace_StillParses()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();

        // Model wraps JSON in prose — the extractor should find { ... } correctly.
        var response = """
            Here is the verdict in JSON format:
            { "mergeResolutions": [], "rankedIssues": [], "dismissedIssues": [], "summary": "Clean PR." }
            """;

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(response);

        var verdict = await svc.RenderVerdictAsync(session);

        Assert.False(verdict.ParseFailed);
        Assert.Equal("Clean PR.", verdict.Summary);
    }

    // ── Model selection ──────────────────────────────────────────────────────

    [Fact]
    public async Task RenderVerdictAsync_UsesSummaryModel()
    {
        var (svc, claude) = MakeService();
        var session = MakeSession();
        string? capturedModel = null;

        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, int, CancellationToken>((_, model, _, _) => capturedModel = model)
              .ReturnsAsync("""{ "mergeResolutions": [], "rankedIssues": [], "dismissedIssues": [], "summary": "" }""");

        await svc.RenderVerdictAsync(session);

        Assert.Equal(new ColosseumOptions().SummaryModel, capturedModel);
    }
}
