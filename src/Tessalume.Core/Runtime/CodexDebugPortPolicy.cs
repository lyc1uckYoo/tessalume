namespace Tessalume.Core.Runtime;

/// <summary>
/// Defines the bounded loopback ports Tessalume may inspect before launching its
/// own Codex instance. User and last-known ports stay first, followed by known
/// third-party hosts and finally Tessalume's managed range.
/// </summary>
public static class CodexDebugPortPolicy
{
    public const int CodexPlusPlusPort = 9229;

    public const int ManagedPortStart = 9340;

    public const int ManagedPortEnd = 9399;

    public static bool IsValid(int port) => port is >= 1024 and <= 65535;

    public static IReadOnlyList<int> BuildProbeOrder(params int?[] preferredPorts)
    {
        var ports = new List<int>();
        var seen = new HashSet<int>();

        void Add(int? port)
        {
            if (port is { } value && IsValid(value) && seen.Add(value))
            {
                ports.Add(value);
            }
        }

        foreach (var port in preferredPorts)
        {
            Add(port);
        }

        Add(CodexPlusPlusPort);
        for (var port = ManagedPortStart; port <= ManagedPortEnd; port++)
        {
            Add(port);
        }

        return ports;
    }
}
