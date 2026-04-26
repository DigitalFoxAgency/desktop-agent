namespace AgentDesktop.Application.Modules;

/// <summary>
/// Reads <c>[&lt;skill&gt;: verified]</c> markers from the launchpad's
/// SESSION-LOG.md. Used by the scenario runner to enforce launchpad
/// constitution §8 — a downstream step must not start until the
/// upstream skill has been verified.
/// </summary>
public interface ISessionLog
{
    /// <summary>Returns the set of skills that have a <c>[&lt;skill&gt;: verified]</c> marker for the given client path.</summary>
    Task<IReadOnlySet<string>> ReadVerifiedSkillsAsync(string clientPath, CancellationToken ct);

    /// <summary>Append a <c>[&lt;skill&gt;: verified]</c> line. Used after a successful skill execution.</summary>
    Task RecordVerifiedAsync(string clientPath, string skillId, CancellationToken ct);
}
