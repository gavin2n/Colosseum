using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace Colosseum.Web.Hubs;

public class ArenaHub(
    DebateOrchestrator orchestrator,
    ModeratorService moderator,
    GitHubService gitHub,
    ILogger<ArenaHub> logger) : Hub
{
    // In-memory session store (v1 — single session)
    private static ReviewSession? _session;
    private static CancellationTokenSource? _cts;

    public async Task StartReview(string prUrl, List<string> gladiatorNames, int rounds)
    {
        try
        {
            // Validate squad size
            if (gladiatorNames.Count < 2)
            {
                await Clients.Caller.SendAsync("Error", "Minimum 2 gladiators required.");
                return;
            }

            // Parse PR URL eagerly so we fail fast on bad input
            var squad = Gladiator.DefaultSquad
                .Where(g => gladiatorNames.Contains(g.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (squad.Count < 2)
            {
                await Clients.Caller.SendAsync("Error", "Could not resolve gladiators from names provided.");
                return;
            }

            await Clients.Caller.SendAsync("StatusUpdate", "Fetching PR diff…");

            PrInfo pr;
            try
            {
                pr = await gitHub.FetchPrAsync(prUrl);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", ex.Message);
                return;
            }

            _cts = new CancellationTokenSource();
            _session = new ReviewSession
            {
                PrUrl = prUrl,
                Owner = pr.Owner,
                Repo = pr.Repo,
                PrNumber = pr.Number,
                PrTitle = pr.Title,
                Diff = pr.Diff,
                Squad = squad,
                MaxRounds = Math.Clamp(rounds, 1, 3)
            };

            await Clients.Caller.SendAsync("SessionStarted",
                _session.Owner, _session.Repo, _session.PrNumber, _session.PrTitle);

            var progress = new Progress<DebateEvent>(async evt =>
            {
                try
                {
                    switch (evt)
                    {
                        case GladiatorTyping t:
                            await Clients.Caller.SendAsync("GladiatorTyping", t.GladiatorName, t.AccentColour);
                            break;
                        case TurnCompleted t:
                            await Clients.Caller.SendAsync("TurnCompleted", t.Turn);
                            break;
                        case IssueRaised r:
                            await Clients.Caller.SendAsync("IssueRaised", r.Issue);
                            break;
                        case IssueUpdated u:
                            await Clients.Caller.SendAsync("IssueUpdated", u.Issue);
                            break;
                        case DebateComplete:
                            await Clients.Caller.SendAsync("DebateComplete");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error sending SignalR event");
                }
            });

            await orchestrator.RunAsync(_session, progress, _cts.Token);

            if (_session.Status == SessionStatus.Complete)
            {
                await Clients.Caller.SendAsync("StatusUpdate", "Rendering Arbiter verdict…");
                var verdict = await moderator.RenderVerdictAsync(_session);
                _session.Verdict = verdict;
                await Clients.Caller.SendAsync("VerdictReady", verdict);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartReview failed");
            await Clients.Caller.SendAsync("Error", "An unexpected error occurred.");
        }
    }

    public Task StopReview()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public Task<object?> GetSessionState()
    {
        if (_session is null) return Task.FromResult<object?>(null);
        return Task.FromResult<object?>(new
        {
            _session.Id,
            _session.Status,
            _session.Turns,
            _session.Issues,
            _session.Verdict
        });
    }
}
