using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Application.Policies;

public sealed class DefaultPolicyEngine : IPolicyEngine
{
    private static readonly string[] DeleteShellTokens = { "rm", "rmdir", "unlink" };
    private static readonly string[] PackageManagers = { "npm", "pnpm", "yarn", "pip", "uv", "poetry", "dotnet" };

    public Task<PolicyDecision> ClassifyAsync(IntentDescriptor intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (string.Equals(intent.Tool, "Bash", StringComparison.OrdinalIgnoreCase) && intent.CommandLine is { } cmd)
        {
            var head = FirstToken(cmd);

            if (DeleteShellTokens.Contains(head, StringComparer.OrdinalIgnoreCase))
            {
                return Decide(ActionClassification.DeleteFile, semaphore: false, "Shell delete command");
            }

            if (head.Equals("git", StringComparison.OrdinalIgnoreCase) && cmd.Contains(" push", StringComparison.OrdinalIgnoreCase))
            {
                return Decide(ActionClassification.GitPush, semaphore: false, "git push");
            }

            if (PackageManagers.Contains(head, StringComparer.OrdinalIgnoreCase))
            {
                var isInstall = cmd.Contains(" install", StringComparison.OrdinalIgnoreCase)
                    || cmd.Contains(" add", StringComparison.OrdinalIgnoreCase);
                return isInstall
                    ? Decide(ActionClassification.InstallPackage, semaphore: true, $"{head} install/add")
                    : Decide(ActionClassification.BuildClass, semaphore: true, $"{head} build-class command");
            }

            return Decide(ActionClassification.RunShell, semaphore: false, "Generic shell command");
        }

        if (string.Equals(intent.Tool, "Edit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent.Tool, "Write", StringComparison.OrdinalIgnoreCase))
        {
            return Decide(ActionClassification.Safe, semaphore: false, "File write within working dir");
        }

        return Decide(ActionClassification.Safe, semaphore: false, "Unclassified intent");
    }

    private static Task<PolicyDecision> Decide(ActionClassification classification, bool semaphore, string reason)
    {
        var requiresConfirm = classification != ActionClassification.Safe;
        return Task.FromResult(new PolicyDecision(classification, requiresConfirm, semaphore, reason));
    }

    private static string FirstToken(string commandLine)
    {
        var trimmed = commandLine.TrimStart();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }
}
