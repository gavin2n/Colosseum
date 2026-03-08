using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Colosseum.Core.Services;

public record DebateEvent;
public record TurnCompleted(DebateTurn Turn) : DebateEvent;
public record IssueRaised(Issue Issue) : DebateEvent;
public record IssueUpdated(Issue Issue) : DebateEvent;
public record DebateComplete : DebateEvent;
public record GladiatorTyping(string GladiatorName, string AccentColour) : DebateEvent;

public class DebateOrchestrator(
    IClaudeProvider claude,
    GladiatorService gladiatorService,
    IssueTracker issueTracker,
    SimilarityService similarityService,
    IOptions<ColosseumOptions> options,
    ILogger<DebateOrchestrator> logger)
{
    private readonly ColosseumOptions _opts = options.Value;

    public async Task RunAsync(
        ReviewSession session,
        IProgress<DebateEvent> progress,
        CancellationToken ct = default)
    {
        session.Status = SessionStatus.Running;

        var activeGladiators = session.Squad.Where(g => !g.IsArbiter).ToList();
        int turnIndex = 0;

        for (int round = 1; round <= session.MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var gladiator in activeGladiators)
            {
                ct.ThrowIfCancellationRequested();

                progress.Report(new GladiatorTyping(gladiator.Name, gladiator.AccentColour));

                string turnText;
                try
                {
                    var prompt = gladiatorService.BuildPrompt(
                        gladiator,
                        session.Diff,
                        session.Turns,
                        session.Issues,
                        session.Squad,
                        round);

                    turnText = await claude.CompleteAsync(prompt, _opts.DebateModel, _opts.MaxTurnTokens, ct);
                }
                catch (OperationCanceledException)
                {
                    session.Status = SessionStatus.Cancelled;
                    progress.Report(new DebateComplete());
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Claude error for {Gladiator} round {Round} — skipping turn", gladiator.Name, round);
                    continue;
                }

                var mentions = MentionParser.Extract(turnText, activeGladiators, gladiator.Name);
                var referencedIssueIds = new List<Guid>();

                // Parse issues and overlaps
                var (newIssues, overlapTitles) = issueTracker.ParseAndApply(
                    turnText, gladiator, session.Issues);

                // Similarity detection for each new issue
                foreach (var issue in newIssues)
                {
                    session.Issues.Add(issue);
                    progress.Report(new IssueRaised(issue));

                    try
                    {
                        var existingIssues = session.Issues
                            .Where(i => i.Id != issue.Id)
                            .ToList();

                        var candidates = await similarityService.FindCandidatesAsync(
                            issue, existingIssues, ct);

                        foreach (var (candidate, _) in candidates)
                        {
                            issueTracker.FlagMergeCandidate(issue, candidate);
                            progress.Report(new IssueUpdated(issue));
                            progress.Report(new IssueUpdated(candidate));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Similarity detection failed for issue '{Title}' — continuing", issue.Title);
                    }
                }

                // Report updates for issues referenced via [OVERLAP: ...]
                foreach (var title in overlapTitles)
                {
                    var matched = session.Issues.FirstOrDefault(i =>
                        i.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                    if (matched is not null)
                    {
                        referencedIssueIds.Add(matched.Id);
                        progress.Report(new IssueUpdated(matched));
                    }
                }

                var turn = new DebateTurn(
                    Guid.NewGuid(),
                    gladiator.Id,
                    gladiator.Name,
                    gladiator.AccentColour,
                    round,
                    turnIndex++,
                    turnText,
                    mentions,
                    referencedIssueIds,
                    DateTimeOffset.UtcNow);

                session.Turns.Add(turn);
                progress.Report(new TurnCompleted(turn));
            }
        }

        session.Status = SessionStatus.Complete;
        session.CompletedAt = DateTimeOffset.UtcNow;
        progress.Report(new DebateComplete());
    }
}
