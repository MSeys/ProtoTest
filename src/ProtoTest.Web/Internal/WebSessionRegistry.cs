namespace ProtoTest.Web.Internal;

/// <summary>Tracks the web sessions opened during one test so they can be finalized at teardown.</summary>
internal sealed class WebSessionRegistry
{
    private readonly List<WebSession> _sessions = [];

    public IReadOnlyList<WebSession> Sessions => _sessions;

    public bool TryGet(string name, out WebSession session)
    {
        foreach (var candidate in _sessions)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                session = candidate;
                return true;
            }
        }

        session = null!;
        return false;
    }

    public void Add(WebSession session) => _sessions.Add(session);
}
