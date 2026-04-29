namespace AgentPlatform.Bridge.Config;

public sealed class BridgeOptions
{
    /// <summary>Per-run identifier injected by the container driver.</summary>
    public Guid RunId { get; set; }

    /// <summary>Per-phase identifier injected by the container driver.</summary>
    public Guid PhaseRunId { get; set; }

    /// <summary>Working dir bind-mounted from the host volume.</summary>
    public string WorkingDir { get; set; } = "/workspace";

    /// <summary>WebSocket URL of the API bridge endpoint (no token query string — the token is sent as a header).</summary>
    public string ApiWebSocketUrl { get; set; } = "ws://host.docker.internal:5080/ws/bridge";

    /// <summary>Bearer token used to authenticate the bridge connection.</summary>
    public string BridgeToken { get; set; } = string.Empty;

    /// <summary>The launchpad skill the wrapper should run.</summary>
    public string Skill { get; set; } = string.Empty;

    /// <summary>Path to the claude binary inside the run image.</summary>
    public string ClaudeBinary { get; set; } = "claude";
}
