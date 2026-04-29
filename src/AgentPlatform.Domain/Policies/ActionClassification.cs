namespace AgentPlatform.Domain.Policies;

public enum ActionClassification
{
    Safe = 0,
    DeleteFile = 1,
    GitPush = 2,
    InstallPackage = 3,
    RunShell = 4,
    BuildClass = 5,
}
