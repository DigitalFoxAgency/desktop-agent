using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Domain.Modules;

public sealed class WorkflowDef
{
    public Guid Id { get; init; }
    public Guid ModuleVersionId { get; init; }
    public string WorkflowId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InputsSchemaJson { get; set; } = "{}";
    public List<PhaseDef> Phases { get; init; } = new();
}

public sealed class PhaseDef
{
    public Guid Id { get; init; }
    public Guid WorkflowDefId { get; init; }
    public int Order { get; init; }
    public string PhaseId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Role Role { get; set; }
    public string Skill { get; set; } = string.Empty;
    public PhaseKind Kind { get; set; }
}

public enum PhaseKind
{
    Standard = 0,
    Verification = 1,
    Manual = 2,
}
