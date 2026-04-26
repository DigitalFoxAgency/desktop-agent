namespace AgentDesktop.Domain.Chat;

/// <summary>
/// Lightweight reference to a (module, skill) pair that produced a
/// message via direct skill invocation (not through a scenario).
/// </summary>
public readonly record struct SkillRef(ModuleId ModuleId, SkillId SkillId);
