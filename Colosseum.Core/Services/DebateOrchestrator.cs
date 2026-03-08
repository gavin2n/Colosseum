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
        Func<DebateEvent, Task> onEvent,
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

                await onEvent(new GladiatorTyping(gladiator.Name, gladiator.AccentColour));

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
                    await onEvent(new DebateComplete());
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

                // For each new issue: add to session, run similarity, THEN fire IssueRaised
                // so the client receives the issue already tagged as a merge candidate if applicable.
                foreach (var issue in newIssues)
                {
                    session.Issues.Add(issue);

                    try
                    {
                        var existingIssues = session.Issues
                            .Where(i => i.Id != issue.Id)
                            .ToList();

                        var candidates = await similarityService.FindCandidatesAsync(
                            issue, existingIssues, ct);

                        foreach (var (candidate, _) in candidates)
                            issueTracker.FlagMergeCandidate(issue, candidate);

                        // IssueRaised fires after merge-candidate status is set
                        await onEvent(new IssueRaised(issue));

                        // Notify clients that existing candidates were also updated
                        foreach (var (candidate, _) in candidates)
                            await onEvent(new IssueUpdated(candidate));
                    }
                    catch (OperationCanceledException)
                    {
                        session.Status = SessionStatus.Cancelled;
                        await onEvent(new DebateComplete());
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Similarity detection failed for issue '{Title}' — firing IssueRaised anyway", issue.Title);
                        await onEvent(new IssueRaised(issue));
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
                        await onEvent(new IssueUpdated(matched));
                    }
                }

                var turn = new DebateTurn(
                    Guid.NewGuid(),
                    gladiator.Id,
                    gladiator.Name,
                    gladiator.AccentColour,
                    gladiator.Domain,
                    round,
                    turnIndex++,
                    turnText,
                    mentions,
                    referencedIssueIds,
                    DateTimeOffset.UtcNow);

                session.Turns.Add(turn);
                await onEvent(new TurnCompleted(turn));
            }
        }

        session.Status = SessionStatus.Complete;
        session.CompletedAt = DateTimeOffset.UtcNow;
        await onEvent(new DebateComplete());
    }
}
