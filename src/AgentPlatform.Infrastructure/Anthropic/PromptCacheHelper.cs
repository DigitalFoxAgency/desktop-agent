namespace AgentPlatform.Infrastructure.Anthropic;

/// <summary>
/// Helper for materialising prompt-cache breakpoints in env vars consumed by the
/// run-container Bridge. Accepts the static project context (CLAUDE.md, ORDER.md, prior
/// research summary) and exposes them as the cached system block claude reads at boot.
/// Phase 4 keeps this minimal — it just sets <c>ANTHROPIC_PROMPT_CACHE=1</c> and computes a
/// cache key the API can pass to the wrapper. Real per-skill cache keys land alongside
/// the launchpad's prompt-caching contract.
/// </summary>
public static class PromptCacheHelper
{
    public static IReadOnlyDictionary<string, string> EnrichEnvironment(
        IReadOnlyDictionary<string, string> baseEnv,
        Guid tenantId,
        string moduleId,
        string skill)
    {
        var enriched = new Dictionary<string, string>(baseEnv, StringComparer.Ordinal)
        {
            ["ANTHROPIC_PROMPT_CACHE"] = "1",
            ["AGP_PROMPT_CACHE_KEY"] = $"{tenantId:N}:{moduleId}:{skill}",
        };
        return enriched;
    }
}
