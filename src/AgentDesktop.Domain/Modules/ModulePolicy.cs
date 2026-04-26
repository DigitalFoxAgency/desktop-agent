namespace AgentDesktop.Domain.Modules;

/// <summary>
/// A module's declaration of how a class of actions it will propose
/// should be classified by the platform's policy engine. The class
/// names are either baseline <c>DangerousActionKind</c> values
/// (DeleteFile / GitPush / InstallPackage / RunShell) or
/// module-specific strings (which the engine treats as Dangerous
/// per default-deny per R7).
/// </summary>
public sealed record ModulePolicy
{
    public ModulePolicy(string actionClass, ActionClassification classification, string reason)
    {
        ArgumentNullException.ThrowIfNull(actionClass);
        ArgumentNullException.ThrowIfNull(reason);

        if (string.IsNullOrWhiteSpace(actionClass))
        {
            throw new ArgumentException("Action class cannot be empty.", nameof(actionClass));
        }

        ActionClass = actionClass;
        Classification = classification;
        Reason = reason;
    }

    public string ActionClass { get; }
    public ActionClassification Classification { get; }
    public string Reason { get; }
}
