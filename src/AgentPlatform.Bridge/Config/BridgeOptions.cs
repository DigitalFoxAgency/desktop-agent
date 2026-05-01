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

    /// <summary>When true, the Bridge swaps the real claude wrapper for a scripted mock — useful for plumbing smoke tests without an Anthropic key.</summary>
    public bool Mock { get; set; }

    /// <summary>In-container path to the run's module root (e.g. <c>/opt/modules/df-client-launchpad</c>). When set, the Bridge wires the module's <c>source/template/.claude/skills</c> into the claude CLI's home so its slash commands resolve, and the wrapper adds the module dir to claude's tool-access scope.</summary>
    public string? ModuleDir { get; set; }

    /// <summary>When true, the IntentInterceptor classifies tool-use events but never emits <c>confirmation_request</c> frames — every action forwards as a passthrough <c>tool_use</c>. Pair with <c>--permission-mode=bypassPermissions</c> on the claude CLI so claude itself doesn't prompt either.</summary>
    public bool AutoConfirm { get; set; }
}

