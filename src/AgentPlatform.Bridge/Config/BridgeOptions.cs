namespace AgentPlatform.Bridge.Config;

public sealed class BridgeOptions
{
    /// <summary>Per-run identifier injected by the container driver.</summary>
    public Guid RunId { get; set; }

    /// <summary>Per-phase identifier injected by the container driver.</summary>
    public Guid PhaseRunId { get; set; }

    /// <summary>Working dir bind-mounted from the host volume.</summary>
    public string WorkingDir { get; set; } = "/work";

    /// <summary>WebSocket URL of the API bridge endpoint.</summary>
    public string ApiWebSocketUrl { get; set; } = "ws://api:8080/ws/bridge";

    /// <summary>Bearer token used to authenticate the bridge connection.</summary>
    public string BridgeToken { get; set; } = string.Empty;
}
