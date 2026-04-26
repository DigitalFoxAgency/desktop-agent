using AgentDesktop.Application.Modules;
using AgentDesktop.Domain;
using AgentDesktop.Infrastructure.Manifests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Tests.Manifests;

/// <summary>
/// T088: load the real bundled <c>modules/df-client-launchpad/module.json</c>
/// from disk through the production pipeline (FileSystemModuleSource +
/// ModuleManifestValidator + ModuleRegistry) and verify the launchpad
/// loads with the three declared operations.
/// </summary>
public sealed class BundledContentTests : IDisposable
{
    private readonly ModuleRegistry _registry;

    public BundledContentTests()
    {
        var modulesRoot = LocateModulesRoot();
        var source = new FileSystemModuleSource(
            Options.Create(new FileSystemModuleSourceOptions { RootPath = modulesRoot }),
            NullLogger<FileSystemModuleSource>.Instance);
        var validator = new ModuleManifestValidator();
        _registry = new ModuleRegistry(source, validator, NullLogger<ModuleRegistry>.Instance);
    }

    public void Dispose() => _registry.Dispose();

    [Fact]
    public async Task Launchpad_module_loads_with_three_operations()
    {
        await _registry.RefreshAsync(CancellationToken.None);

        var launchpad = _registry.Find(new ModuleId("df-client-launchpad"));
        launchpad.Should().NotBeNull();
        launchpad!.LoadStatus.Should().Be(ModuleLoadStatus.Loaded);
        launchpad.LoadError.Should().BeNull();

        launchpad.Operations
            .Select(o => o.Id)
            .Should().BeEquivalentTo(new[] { "onboard-client", "launch-ads", "monthly-report" });

        // Each operation must have a non-empty name + description.
        launchpad.Operations.Should().AllSatisfy(op =>
        {
            op.Name.Should().NotBeNullOrEmpty();
            op.Description.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task Launchpad_declares_baseline_dangerous_action_classes()
    {
        await _registry.RefreshAsync(CancellationToken.None);

        var launchpad = _registry.Find(new ModuleId("df-client-launchpad"))!;
        var classes = launchpad.Policies.Select(p => p.ActionClass).ToList();
        classes.Should().Contain(new[] { "DeleteFile", "GitPush", "InstallPackage", "RunShell" });
    }

    [Fact]
    public async Task Launchpad_internal_skills_are_introspectable()
    {
        await _registry.RefreshAsync(CancellationToken.None);

        var launchpad = _registry.Find(new ModuleId("df-client-launchpad"))!;
        // The launchpad declares 17 internal skills in its module.json.
        launchpad.Skills.Should().HaveCount(17);
    }

    private static string LocateModulesRoot()
    {
        // Walk up from the test assembly's bin dir until we find the modules/ folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "modules", "df-client-launchpad", "module.json");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "modules");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Cannot locate modules/ root from test execution directory.");
    }
}
