namespace AgentDesktop.Domain;

/// <summary>Identifier for a <see cref="Chat.Conversation"/>.</summary>
public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

/// <summary>Identifier for a <see cref="Chat.Message"/>.</summary>
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

/// <summary>Identifier for a <see cref="Policies.PolicyDecision"/>.</summary>
public readonly record struct PolicyDecisionId(Guid Value)
{
    public static PolicyDecisionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

/// <summary>Identifier for a <see cref="Modules.Module"/>. Reverse-DNS or kebab-case slug.</summary>
public readonly record struct ModuleId
{
    public ModuleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ModuleId cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>Identifier for a <see cref="Modules.Skill"/>, unique within its owning module.</summary>
public readonly record struct SkillId
{
    public SkillId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SkillId cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>Opaque account identifier issued by the subscription service.</summary>
public readonly record struct AccountId
{
    public AccountId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("AccountId cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>
/// Composite key into the secret store: a namespace plus an entry name.
/// Sensitive values are never carried by this type.
/// </summary>
public readonly record struct SecretKey
{
    public SecretKey(string ns, string name)
    {
        if (string.IsNullOrWhiteSpace(ns))
        {
            throw new ArgumentException("Secret namespace cannot be empty.", nameof(ns));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name cannot be empty.", nameof(name));
        }

        Namespace = ns;
        Name = name;
    }

    public string Namespace { get; }
    public string Name { get; }
    public override string ToString() => $"{Namespace}/{Name}";
}
