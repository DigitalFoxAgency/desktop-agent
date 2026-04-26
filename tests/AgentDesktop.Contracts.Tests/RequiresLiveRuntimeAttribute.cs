namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Marker for tests that require a real OpenClaw/NemoClaw runtime to
/// be installed locally. CI excludes these by default; opt-in via
/// <c>dotnet test --filter Category=RequiresLiveRuntime</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresLiveRuntimeAttribute : Attribute
{
    public string Reason { get; }

    public RequiresLiveRuntimeAttribute(string reason = "Requires real OpenClaw/NemoClaw installation.")
    {
        Reason = reason;
    }
}
