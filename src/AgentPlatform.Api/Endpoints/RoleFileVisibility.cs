using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Api.Endpoints;

/// <summary>
/// Filters the per-phase file tree by the caller's roles. Non-technical roles
/// (marketer, strategist, designer, media-buyer) see only business-deliverable
/// folders inside <c>clients/&lt;slug&gt;/</c>; engineer and admin see
/// everything.
///
/// The allow-list is module-agnostic on purpose — it matches the launchpad's
/// numbered phase-output convention (<c>NN-name</c>) and a small set of
/// well-known top-level files. When a future module ships a different
/// convention this becomes config-driven via <c>module.json</c>.
/// </summary>
public static class RoleFileVisibility
{
    private static readonly string[] BusinessAllowedAtClientRoot =
    {
        "00-pre-research",
        "01-brief",
        "02-research",
        "03-strategy",
        "04-keywords",
        "05-design",
        "06-architecture",
        "08-ads",
        "09-client-handover",
        "10-reporting",
        "SESSION-LOG.md",
        "LEARNINGS.md",
        "CLAUDE.md",
    };

    private static readonly string[] BusinessAllowedAtWorkspaceRoot =
    {
        "clients",
        "SESSION-LOG.md",
    };

    public static bool IsTechnicalRole(IReadOnlyCollection<Role> roles)
        => roles.Contains(Role.Admin) || roles.Contains(Role.Engineer);

    public static bool IsVisible(string relativePath, IReadOnlyCollection<Role> roles)
    {
        if (IsTechnicalRole(roles)) { return true; }
        var rel = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(rel)) { return true; }
        var parts = rel.Split('/');

        if (parts[0] == "clients")
        {
            // "clients" itself, or "clients/<slug>" — let the user navigate in.
            if (parts.Length <= 2) { return true; }
            // Anything below a client folder must be in the business allow-list.
            return BusinessAllowedAtClientRoot.Contains(parts[2]);
        }

        return BusinessAllowedAtWorkspaceRoot.Contains(parts[0]);
    }
}
