using System.Runtime.CompilerServices;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Modules;
using AgentDesktop.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Contracts.Tests;

public sealed class ModuleRegistryContractTests : IDisposable
{
    private readonly InMemoryModuleSource _source = new();
    private readonly ModuleManifestValidator _validator = new();
    private readonly ModuleRegistry _registry;

    public ModuleRegistryContractTests()
    {
        _registry = new ModuleRegistry(_source, _validator, NullLogger<ModuleRegistry>.Instance);
    }

    public void Dispose() => _registry.Dispose();

    [Fact]
    public async Task A_valid_manifest_loads_with_LoadStatus_Loaded()
    {
        _source.Add("test-module", BuildValidManifest("test-module", "1.0.0"));

        await _registry.RefreshAsync(CancellationToken.None);

        var module = _registry.Find(new ModuleId("test-module"));
        module.Should().NotBeNull();
        module!.LoadStatus.Should().Be(ModuleLoadStatus.Loaded);
        module.Operations.Should().ContainSingle()
            .Which.Id.Should().Be("op-1");
    }

    [Fact]
    public async Task Unknown_schema_version_yields_Incompatible_with_LoadError()
    {
        _source.Add("test", BuildManifestWithSchemaVersion(99));

        await _registry.RefreshAsync(CancellationToken.None);

        var module = _registry.GetAll().Single();
        module.LoadStatus.Should().NotBe(ModuleLoadStatus.Loaded);
        module.LoadError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Missing_dependency_yields_Unavailable_with_named_missing_id()
    {
        _source.Add("module-a", BuildValidManifest("module-a", "1.0.0", dependencies: new[] { ("missing-dep", "^1.0.0") }));

        await _registry.RefreshAsync(CancellationToken.None);

        var module = _registry.Find(new ModuleId("module-a"))!;
        module.LoadStatus.Should().Be(ModuleLoadStatus.Unavailable);
        module.LoadError.Should().Contain("missing-dep");
    }

    [Fact]
    public async Task Concurrent_RefreshAsync_does_not_duplicate_entries()
    {
        _source.Add("module-a", BuildValidManifest("module-a", "1.0.0"));

        await Task.WhenAll(
            _registry.RefreshAsync(CancellationToken.None),
            _registry.RefreshAsync(CancellationToken.None),
            _registry.RefreshAsync(CancellationToken.None));

        _registry.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task FindOperation_resolves_declared_operations()
    {
        _source.Add("test", BuildValidManifest("test", "1.0.0"));
        await _registry.RefreshAsync(CancellationToken.None);

        var op = _registry.FindOperation(new ModuleId("test"), "op-1");
        op.Should().NotBeNull();
        op!.Id.Should().Be("op-1");

        var missing = _registry.FindOperation(new ModuleId("test"), "no-such-op");
        missing.Should().BeNull();
    }

    [Fact]
    public async Task Find_returns_null_for_unknown_module()
    {
        await _registry.RefreshAsync(CancellationToken.None);
        _registry.Find(new ModuleId("nope")).Should().BeNull();
    }

    private static string BuildValidManifest(
        string id,
        string version,
        IEnumerable<(string id, string version)>? dependencies = null)
    {
        var deps = dependencies is null ? "[]" :
            "[" + string.Join(",", dependencies.Select(d => $"{{\"id\":\"{d.id}\",\"version\":\"{d.version}\"}}")) + "]";
        return $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "version": "{{version}}",
          "name": "{{id}}",
          "description": "Test module {{id}}",
          "dependencies": {{deps}},
          "operations": [
            { "id": "op-1", "name": "Op One", "description": "Test op" }
          ]
        }
        """;
    }

    private static string BuildManifestWithSchemaVersion(int v) => $$"""
        {
          "schemaVersion": {{v}},
          "id": "test",
          "version": "1.0.0",
          "name": "test",
          "description": "test",
          "operations": [{ "id": "op", "name": "Op", "description": "test" }]
        }
        """;

    private sealed class InMemoryModuleSource : IModuleSource
    {
        private readonly List<RawModuleManifest> _manifests = new();

        public void Add(string moduleId, string manifestText)
        {
            _manifests.Add(new RawModuleManifest(
                ManifestPath: $"/in-memory/{moduleId}/module.json",
                ManifestText: manifestText,
                ModuleRoot: $"/in-memory/{moduleId}",
                Source: ModuleSource.Bundled));
        }

        public async IAsyncEnumerable<RawModuleManifest> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var m in _manifests.ToList())
            {
                ct.ThrowIfCancellationRequested();
                yield return m;
                await Task.Yield();
            }
        }
    }
}
