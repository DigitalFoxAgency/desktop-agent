using AgentDesktop.Domain;

namespace AgentDesktop.Application.Policies;

/// <summary>
/// Baseline classification of <see cref="DangerousActionKind"/>
/// values shipped by the platform. Module manifests can override
/// these via <see cref="Domain.Modules.ModulePolicy"/> entries
/// keyed by action class.
/// </summary>
public static class BaselineClassificationTable
{
    /// <summary>
    /// Returns the platform's baseline classification for a given
    /// action kind. Anything not explicitly safe defaults to
    /// <see cref="ActionClassification.Dangerous"/> (default-deny).
    /// </summary>
    public static ActionClassification Classify(DangerousActionKind kind) => kind switch
    {
        DangerousActionKind.DeleteFile => ActionClassification.Dangerous,
        DangerousActionKind.GitPush => ActionClassification.Dangerous,
        DangerousActionKind.InstallPackage => ActionClassification.Dangerous,
        DangerousActionKind.RunShell => ActionClassification.Dangerous,
        DangerousActionKind.ModuleDeclared => ActionClassification.Dangerous,
        _ => ActionClassification.Dangerous,
    };
}
