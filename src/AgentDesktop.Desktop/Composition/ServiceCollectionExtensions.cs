using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Modules;
using AgentDesktop.Application.Policies;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Application.Secrets;
using AgentDesktop.Application.Subscription;
using AgentDesktop.Desktop.Adapters;
using AgentDesktop.Desktop.ViewModels;
using AgentDesktop.Infrastructure.Manifests;
using AgentDesktop.Infrastructure.Persistence.Sqlite;
using AgentDesktop.Infrastructure.Secrets;
using AgentDesktop.Infrastructure.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Desktop.Composition;

/// <summary>Builds the production DI container for the desktop shell.</summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceProvider BuildServiceProvider(AgentDesktopOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole());

        var dataDir = ResolveDataDirectory();
        Directory.CreateDirectory(dataDir);

        services.Configure<SqliteDatabaseOptions>(o =>
            o.DatabasePath = Path.Combine(dataDir, "agent-desktop.db"));
        services.Configure<EncryptedFileSecretStoreOptions>(o =>
        {
            o.FilePath = Path.Combine(dataDir, "secrets.bin");
            o.SaltPath = Path.Combine(dataDir, "secrets.salt");
        });
        services.Configure<FileSystemModuleSourceOptions>(o =>
            o.RootPath = ResolveModulesRoot());
        services.Configure<SubscriptionGateOptions>(_ => { });

        // Application
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<ISubscriptionGate, SubscriptionGate>();
        services.AddSingleton<IPolicyEngine, DefaultPolicyEngine>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<ModuleManifestValidator>();
        services.AddSingleton<IConversationQuestionState, InMemoryConversationQuestionState>();
        services.AddSingleton<DelegationRunner>();
        services.AddSingleton<SkillInvocationService>();

        // Infrastructure
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IChatRepository, SqliteChatRepository>();
        services.AddSingleton<IAuditLog, SqliteAuditLog>();
        services.AddSingleton<ISecretStore, EncryptedFileSecretStore>();
        services.AddSingleton<IModuleSource, FileSystemModuleSource>();

        // UI-side adapters (T082, T085)
        services.AddSingleton<AvaloniaConfirmationPrompt>();
        services.AddSingleton<IConfirmationPrompt>(sp => sp.GetRequiredService<AvaloniaConfirmationPrompt>());
        services.AddSingleton<IHumanHandoffPrompt, NotImplementedHumanHandoffPrompt>();

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddSingleton<ConversationListViewModel>();
        services.AddSingleton<ChatViewModel>();

        if (options.UseFakeRuntime)
        {
            services.AddSingleton<ISubscriptionValidator, AlwaysActiveSubscriptionValidator>();
            services.AddSingleton<IRuntimeManager, EchoRuntimeManager>();
        }
        else
        {
            services.AddHttpClient<ISubscriptionValidator, HttpSubscriptionValidator>(client =>
            {
                client.BaseAddress = new Uri("https://localhost");
            });
            services.AddSingleton<IRuntimeManager>(_ =>
                throw new InvalidOperationException(
                    "Real runtime is not implemented yet (T111). Run with --fake-runtime."));
        }

        services.AddSingleton<RuntimeBootstrapper>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string ResolveDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetTempPath();
        }
        return Path.Combine(baseDir, "AgentDesktop");
    }

    private static string ResolveModulesRoot()
    {
        var here = AppContext.BaseDirectory;
        var candidate = Path.Combine(here, "modules");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        var dir = new DirectoryInfo(here);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "modules");
            if (Directory.Exists(probe))
            {
                return probe;
            }
            dir = dir.Parent;
        }

        return candidate;
    }
}
