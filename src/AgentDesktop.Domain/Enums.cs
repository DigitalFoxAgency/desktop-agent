namespace AgentDesktop.Domain;

/// <summary>Author of a <see cref="Chat.Message"/>.</summary>
public enum MessageAuthor
{
    User = 0,
    Agent = 1,
    System = 2,
}

/// <summary>Whether an action requires explicit user confirmation.</summary>
public enum ActionClassification
{
    Safe = 0,
    Dangerous = 1,
}

/// <summary>State machine for the local agent runtime.</summary>
public enum RuntimeStatus
{
    NotInstalled = 0,
    Installing = 1,
    Starting = 2,
    Ready = 3,
    Degraded = 4,
    Stopped = 5,
}

/// <summary>Concrete kind of runtime backing the runtime manager.</summary>
public enum RuntimeKind
{
    OpenClaw = 0,
    Fake = 1,
}

/// <summary>Result of attempting to load a module manifest.</summary>
public enum ModuleLoadStatus
{
    Loaded = 0,
    Unavailable = 1,
    Incompatible = 2,
}

/// <summary>Result of attempting to load a scenario definition.</summary>
public enum ScenarioLoadStatus
{
    Loaded = 0,
    Incompatible = 1,
}

/// <summary>Where a module's files live.</summary>
public enum ModuleSource
{
    Bundled = 0,
    Submodule = 1,
    UserInstalled = 2,
}

/// <summary>Whether a skill is automated by the agent or hand-off to the human.</summary>
public enum SkillKind
{
    Automated = 0,
    Human = 1,
}

/// <summary>Categories of dangerous actions the policy engine recognises by default.</summary>
public enum DangerousActionKind
{
    DeleteFile = 0,
    GitPush = 1,
    InstallPackage = 2,
    RunShell = 3,
    /// <summary>Module manifest declared this skill dangerous via <c>policies[]</c>.</summary>
    ModuleDeclared = 4,
}

/// <summary>Outcome of a policy evaluation.</summary>
public enum PolicyOutcome
{
    Confirmed = 0,
    Declined = 1,
    Skipped = 2,
    Expired = 3,
}

/// <summary>Result of attempting to execute a confirmed dangerous action.</summary>
public enum PolicyExecutionResult
{
    Succeeded = 0,
    Failed = 1,
}

/// <summary>Subscription state surfaced through the subscription gate.</summary>
public enum SubscriptionStatus
{
    Active = 0,
    Grace = 1,
    Expired = 2,
    Revoked = 3,
    Unknown = 4,
}

/// <summary>Origin of a proposed dangerous action.</summary>
public enum PolicyOriginKind
{
    Skill = 0,
    ScenarioStep = 1,
}
