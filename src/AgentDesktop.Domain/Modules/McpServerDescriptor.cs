using System.Collections.ObjectModel;

namespace AgentDesktop.Domain.Modules;

/// <summary>
/// Description of an MCP server a module needs at runtime. Secret values
/// are never embedded here — environment variable values referenced via
/// secret-store keys must be resolved by infrastructure at launch time.
/// </summary>
public sealed record McpServerDescriptor
{
    public McpServerDescriptor(
        string name,
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("MCP server name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("MCP server command cannot be empty.", nameof(command));
        }

        Name = name;
        Command = command;
        Args = new ReadOnlyCollection<string>(args.ToList());
        Env = new ReadOnlyDictionary<string, string>(env.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    public string Name { get; }
    public string Command { get; }
    public IReadOnlyList<string> Args { get; }
    public IReadOnlyDictionary<string, string> Env { get; }
}
