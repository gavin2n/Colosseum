using System.Collections.Concurrent;
using Colosseum.Core.Models;

namespace Colosseum.Web;

/// <summary>
/// Per-connection session store. Each SignalR connection manages its own
/// <see cref="ReviewSession"/> and <see cref="CancellationTokenSource"/>,
/// preventing state from leaking across users.
/// </summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _map = new();

    private sealed record Entry(ReviewSession Session, CancellationTokenSource Cts);

    public void Set(string connectionId, ReviewSession session, CancellationTokenSource cts)
        => _map[connectionId] = new Entry(session, cts);

    public ReviewSession? GetSession(string connectionId)
        => _map.TryGetValue(connectionId, out var e) ? e.Session : null;

    /// <summary>Cancels and removes the session for <paramref name="connectionId"/>.</summary>
    public void Cancel(string connectionId)
    {
        if (_map.TryRemove(connectionId, out var e))
        {
            e.Cts.Cancel();
            e.Cts.Dispose();
        }
    }

    public CancellationTokenSource? GetCts(string connectionId)
        => _map.TryGetValue(connectionId, out var e) ? e.Cts : null;
}
