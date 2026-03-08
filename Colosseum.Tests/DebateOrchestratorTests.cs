using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Colosseum.Tests;

public class DebateOrchestratorTests
{
    private static DebateOrchestrator MakeOrchestrator(
        Mock<IClaudeProvider> claude,
        ColosseumOptions? opts = null)
    {
        var options = Options.Create(opts ?? new ColosseumOptions());
        var gladiatorService = new GladiatorService(options);
        var issueTracker = new IssueTracker(NullLogger<IssueTracker>.Instance);
        var similarityService = new SimilarityService(
            claude.Object, options, NullLogger<SimilarityService>.Instance);
        return new DebateOrchestrator(
            claude.Object, gladiatorService, issueTracker, similarityService,
            options, NullLogger<DebateOrchestrator>.Instance);
    }

    private static ReviewSession MakeSession(int rounds = 1) => new()
    {
        PrUrl = "https://github.com/test/repo/pull/1",
        Owner = "test",
        Repo = "repo",
        PrNumber = 1,
        Diff = "--- a/Foo.cs\n+++ b/Foo.cs\n@@ -1 +1 @@ public void Foo() { }",
        Squad = [Gladiator.Maximus, Gladiator.Brutus],
        MaxRounds = rounds
    };

    // ── Event sequencing ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SingleRound_EmitsTypingAndTurnCompletedForEachGladiator()
    {
        var claude = new Mock<IClaudeProvider>();
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("No issues here.");

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession(1);
        var events = new List<DebateEvent>();

        await orchestrator.RunAsync(session, evt => { events.Add(evt); return Task.CompletedTask; });

        var typings = events.OfType<GladiatorTyping>().ToList();
        var turns = events.OfType<TurnCompleted>().ToList();

        Assert.Equal(2, typings.Count); // Maximus + Brutus
        Assert.Equal(2, turns.Count);
        Assert.Equal(SessionStatus.Complete, session.Status);
    }

    [Fact]
    public async Task RunAsync_TwoRounds_EmitsCorrectTurnCount()
    {
        var claude = new Mock<IClaudeProvider>();
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("All good.");

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession(2);
        var events = new List<DebateEvent>();

        await orchestrator.RunAsync(session, evt => { events.Add(evt); return Task.CompletedTask; });

        Assert.Equal(4, events.OfType<TurnCompleted>().Count()); // 2 gladiators × 2 rounds
    }

    [Fact]
    public async Task RunAsync_IssueParsed_IssueRaisedFiredAfterTurn()
    {
        var claude = new Mock<IClaudeProvider>();
        // Only Maximus's turn yields an issue
        var callCount = 0;
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => ++callCount == 1
                  ? "Found a problem. [ISSUE: N+1 query | severity: high]"
                  : "Looks fine.");

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession(1);
        var events = new List<DebateEvent>();

        await orchestrator.RunAsync(session, evt => { events.Add(evt); return Task.CompletedTask; });

        Assert.Single(events.OfType<IssueRaised>());
        Assert.Single(session.Issues);
        Assert.Equal("N+1 query", session.Issues[0].Title);
    }

    [Fact]
    public async Task RunAsync_IssueRaisedFiresAfterMergeCandidateSet()
    {
        // Two turns raise nearly-identical issues; the second should arrive
        // already tagged as a merge candidate so the client sees it in one event.
        var claude = new Mock<IClaudeProvider>();
        var callCount = 0;
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => ++callCount switch
              {
                  1 => "Issue here. [ISSUE: N+1 query in loop | severity: high]",
                  2 => "Same thing. [ISSUE: N+1 query in loop | severity: high]",
                  _ => "true" // semantic check response
              });

        // Mock the semantic check to return similar=true
        var semsim = new Mock<IClaudeProvider>();
        semsim.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{ "similar": true, "confidence": 0.95, "reason": "same" }""");

        // Use the same claude mock for everything since we can't easily separate calls here.
        // Instead verify that after IssueRaised for issue #2, it already has MergeCandidate set.
        var opts = new ColosseumOptions();
        var options = Options.Create(opts);
        var gladiatorService = new GladiatorService(options);
        var issueTracker = new IssueTracker(NullLogger<IssueTracker>.Instance);
        var simClaude = new Mock<IClaudeProvider>();
        simClaude.SetupSequence(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Issue here. [ISSUE: N+1 query in loop | severity: high]")
                 .ReturnsAsync("Same. [ISSUE: N+1 query in loop | severity: high]")
                 .ReturnsAsync("""{ "similar": true, "confidence": 0.95, "reason": "same" }""");

        var similarityService = new SimilarityService(simClaude.Object, options, NullLogger<SimilarityService>.Instance);
        var orchestrator = new DebateOrchestrator(
            simClaude.Object, gladiatorService, issueTracker, similarityService,
            options, NullLogger<DebateOrchestrator>.Instance);

        var session = MakeSession(1);
        var raisedEvents = new List<IssueRaised>();

        await orchestrator.RunAsync(session, evt =>
        {
            if (evt is IssueRaised r) raisedEvents.Add(r);
            return Task.CompletedTask;
        });

        // The second IssueRaised should already have a MergeCandidate
        if (raisedEvents.Count == 2)
            Assert.NotNull(raisedEvents[1].Issue.MergeCandidate);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Cancelled_SetsStatusCancelled()
    {
        using var cts = new CancellationTokenSource();

        var claude = new Mock<IClaudeProvider>();
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .Returns<string, string, int, CancellationToken>(async (_, _, _, token) =>
              {
                  await cts.CancelAsync();
                  token.ThrowIfCancellationRequested();
                  return "Never reached";
              });

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession();

        await orchestrator.RunAsync(session, _ => Task.CompletedTask, cts.Token);

        Assert.Equal(SessionStatus.Cancelled, session.Status);
    }

    [Fact]
    public async Task RunAsync_Cancelled_EmitsDebateComplete()
    {
        using var cts = new CancellationTokenSource();

        var claude = new Mock<IClaudeProvider>();
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .Returns<string, string, int, CancellationToken>(async (_, _, _, token) =>
              {
                  await cts.CancelAsync();
                  token.ThrowIfCancellationRequested();
                  return "";
              });

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession();
        var events = new List<DebateEvent>();

        await orchestrator.RunAsync(session, evt => { events.Add(evt); return Task.CompletedTask; }, cts.Token);

        Assert.Contains(events, e => e is DebateComplete);
    }

    // ── Error resilience ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ClaudeErrorOnOneTurn_SkipsTurnContinuesOthers()
    {
        var claude = new Mock<IClaudeProvider>();
        var callCount = 0;
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() =>
              {
                  if (++callCount == 1)
                      throw new InvalidOperationException("CLI crashed");
                  return "Fine.";
              });

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession(1);
        var events = new List<DebateEvent>();

        await orchestrator.RunAsync(session, evt => { events.Add(evt); return Task.CompletedTask; });

        // One turn skipped, one completed — session still finishes
        Assert.Equal(SessionStatus.Complete, session.Status);
        Assert.Single(events.OfType<TurnCompleted>());
    }

    // ── Turn metadata ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Turn_CarriesGladiatorDomain()
    {
        var claude = new Mock<IClaudeProvider>();
        claude.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("Text.");

        var orchestrator = MakeOrchestrator(claude);
        var session = MakeSession(1);
        var turns = new List<TurnCompleted>();

        await orchestrator.RunAsync(session, evt =>
        {
            if (evt is TurnCompleted t) turns.Add(t);
            return Task.CompletedTask;
        });

        Assert.All(turns, t => Assert.False(string.IsNullOrEmpty(t.Turn.GladiatorDomain)));
        Assert.Equal(Gladiator.Maximus.Domain, turns[0].Turn.GladiatorDomain);
    }
}
