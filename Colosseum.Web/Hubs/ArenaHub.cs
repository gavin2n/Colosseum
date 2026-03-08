using Colosseum.Core.Models;
using Colosseum.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace Colosseum.Web.Hubs;

public class ArenaHub(
    DebateOrchestrator orchestrator,
    ModeratorService moderator,
    GitHubService gitHub,
    SessionRegistry sessions,
    ILogger<ArenaHub> logger) : Hub
{
    public async Task StartReview(string prUrl, List<string> gladiatorNames, int rounds)
    {
        var connId = Context.ConnectionId;

        // Prevent double-starting on the same connection
        if (sessions.GetSession(connId) is { Status: SessionStatus.Running })
        {
            await Clients.Caller.SendAsync("Error", "A review is already in progress. Stop it first.");
            return;
        }

        try
        {
            if (gladiatorNames.Count < 2)
            {
                await Clients.Caller.SendAsync("Error", "Minimum 2 gladiators required.");
                return;
            }

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
                pr = await gitHub.FetchPrAsync(prUrl, Context.ConnectionAborted);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", ex.Message);
                return;
            }

            var cts = new CancellationTokenSource();
            var session = new ReviewSession
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

            sessions.Set(connId, session, cts);

            await Clients.Caller.SendAsync("SessionStarted",
                session.Owner, session.Repo, session.PrNumber, session.PrTitle);

            // Use a direct async delegate instead of Progress<T> so exceptions propagate correctly
            // and SignalR events are sent on the caller's connection context.
            var caller = Clients.Caller;
            async Task OnEvent(DebateEvent evt)
            {
                switch (evt)
                {
                    case GladiatorTyping t:
                        await caller.SendAsync("GladiatorTyping", t.GladiatorName, t.AccentColour);
                        break;
                    case TurnCompleted t:
                        await caller.SendAsync("TurnCompleted", t.Turn);
                        break;
                    case IssueRaised r:
                        await caller.SendAsync("IssueRaised", r.Issue);
                        break;
                    case IssueUpdated u:
                        await caller.SendAsync("IssueUpdated", u.Issue);
                        break;
                    case DebateComplete:
                        await caller.SendAsync("DebateComplete");
                        break;
                }
            }

            await orchestrator.RunAsync(session, OnEvent, cts.Token);

            if (session.Status == SessionStatus.Complete)
            {
                await Clients.Caller.SendAsync("StatusUpdate", "Rendering Arbiter verdict…");
                var verdict = await moderator.RenderVerdictAsync(session);
                session.Verdict = verdict;
                await Clients.Caller.SendAsync("VerdictReady", verdict);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartReview failed for connection {ConnId}", connId);
            await Clients.Caller.SendAsync("Error", "An unexpected error occurred.");
        }
    }

    public Task StopReview()
    {
        sessions.Cancel(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task<object?> GetSessionState()
    {
        var session = sessions.GetSession(Context.ConnectionId);
        if (session is null) return Task.FromResult<object?>(null);
        return Task.FromResult<object?>(new
        {
            session.Id,
            session.Status,
            session.Turns,
            session.Issues,
            session.Verdict
        });
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Cancel and clean up the session when the client disconnects,
        // so the orchestrator doesn't keep running and consuming API budget.
        sessions.Cancel(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
